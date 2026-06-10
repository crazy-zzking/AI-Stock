using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Services;

/// <summary>
/// 龙虎榜采集服务 — 拉取东财当日龙虎榜汇总 + 每只上榜股的买/卖方席位明细，
/// 按 (code, date) upsert 到 dragon_tiger。东财 datacenter 字段以实盘联调为准。
/// </summary>
public class DragonTigerSyncService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITradingCalendar _tradingCalendar;
    private readonly MarketSnapshotOptions _options;
    private readonly ILogger<DragonTigerSyncService> _logger;

    public DragonTigerSyncService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        ITradingCalendar tradingCalendar,
        IOptions<MarketSnapshotOptions> options,
        ILogger<DragonTigerSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _tradingCalendar = tradingCalendar;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>采集龙虎榜，返回落库条数。tradeDate 为 null 时自动取最近交易日。</summary>
    public async Task<int> SyncAsync(DateTime? tradeDate = null, CancellationToken ct = default)
    {
        var date = tradeDate?.Date ?? await ResolveLatestTradingDayAsync(ct);
        var client = _httpClientFactory.CreateClient("eastmoney"); // 东财龙虎榜走隧道代理

        // 1. 拉榜单汇总
        string json;
        try
        {
            var url = $"{_options.DragonTigerUrl}?reportName=RPT_DAILYBILLBOARD_DETAILSNEW" +
                      $"&columns=ALL&pageSize=1000&pageNumber=1&sortColumns=BILLBOARD_NET_AMT&sortTypes=-1" +
                      $"&filter=(TRADE_DATE='{date:yyyy-MM-dd}')";
            json = await client.GetStringAsync(url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "龙虎榜接口请求失败：{Date}", date.ToString("yyyy-MM-dd"));
            return 0;
        }

        List<DragonTigerEntity> records;
        try
        {
            records = Parse(json, date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "龙虎榜解析失败：{Date}", date.ToString("yyyy-MM-dd"));
            return 0;
        }

        if (records.Count == 0)
        {
            _logger.LogInformation("龙虎榜无数据（或非交易日）：{Date}", date.ToString("yyyy-MM-dd"));
            return 0;
        }

        // 2. 逐只补买/卖方席位明细（含机构判定）
        _logger.LogInformation("龙虎榜 {Count} 只上榜，开始采集席位明细", records.Count);
        var seatsByCode = new Dictionary<string, (List<DragonTigerSeat> Buy, List<DragonTigerSeat> Sell)>();
        foreach (var rec in records)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var buySeats = await FetchSeatsAsync(client, rec.Code, date, "RPT_BILLBOARD_DAILYDETAILSBUY", ct);
                var sellSeats = await FetchSeatsAsync(client, rec.Code, date, "RPT_BILLBOARD_DAILYDETAILSSELL", ct);
                rec.BuySeatsJson = JsonSerializer.Serialize(buySeats, AIStock.Core.Json.AppJson.Default);
                rec.SellSeatsJson = JsonSerializer.Serialize(sellSeats, AIStock.Core.Json.AppJson.Default);
                rec.HasInstitution = buySeats.Any(s => s.IsInstitution);
                seatsByCode[rec.Code] = (buySeats, sellSeats);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "席位明细采集失败：{Code}", rec.Code);
            }

            if (_options.ItemThrottleMs > 0)
                await Task.Delay(_options.ItemThrottleMs, ct);
        }

        // 3. upsert 主表 + 重建当日席位明细
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var ok = 0;
        foreach (var rec in records)
        {
            if (ct.IsCancellationRequested) break;
            var existing = await db.DragonTiger
                .FirstOrDefaultAsync(d => d.Code == rec.Code && d.Date == rec.Date, ct);
            if (existing == null)
            {
                db.DragonTiger.Add(rec);
            }
            else
            {
                existing.Name = rec.Name;
                existing.Reason = rec.Reason;
                existing.NetBuyAmount = rec.NetBuyAmount;
                existing.BuyAmount = rec.BuyAmount;
                existing.SellAmount = rec.SellAmount;
                existing.HasInstitution = rec.HasInstitution;
                existing.BuySeatsJson = rec.BuySeatsJson;
                existing.SellSeatsJson = rec.SellSeatsJson;
            }
            ok++;
        }

        // 席位明细：先删当日这些股票的旧席位，再批量插入
        var codes = records.Select(r => r.Code).ToList();
        var oldSeats = await db.DragonTigerSeat
            .Where(s => s.Date == date && codes.Contains(s.Code))
            .ToListAsync(ct);
        db.DragonTigerSeat.RemoveRange(oldSeats);

        var seatRows = 0;
        foreach (var rec in records)
        {
            if (!seatsByCode.TryGetValue(rec.Code, out var pair)) continue;
            foreach (var s in pair.Buy)
                db.DragonTigerSeat.Add(ToSeatEntity(rec, s, "buy"));
            foreach (var s in pair.Sell)
                db.DragonTigerSeat.Add(ToSeatEntity(rec, s, "sell"));
            seatRows += pair.Buy.Count + pair.Sell.Count;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("龙虎榜采集完成：{Ok} 只 / {Seats} 条席位（{Date}）",
            ok, seatRows, date.ToString("yyyy-MM-dd"));
        return ok;
    }

    /// <summary>取最近一个交易日（含今天）。任务收盘后(19点)跑，当天为交易日则当天龙虎榜已出榜。</summary>
    private async Task<DateTime> ResolveLatestTradingDayAsync(CancellationToken ct)
    {
        var d = DateTime.Now.Date;
        for (var i = 0; i < 10; i++) // 最多回溯 10 天，跨节假日
        {
            if (await _tradingCalendar.IsTradingDayAsync(d, ct)) return d;
            d = d.AddDays(-1);
        }
        return DateTime.Now.Date; // 兜底
    }

    private static DragonTigerSeatEntity ToSeatEntity(DragonTigerEntity rec, DragonTigerSeat s, string side) =>
        new()
        {
            Date = rec.Date,
            Code = rec.Code,
            Name = rec.Name,
            SeatName = s.SeatName,
            Side = side,
            BuyAmount = s.BuyAmount,
            SellAmount = s.SellAmount,
            NetAmount = s.BuyAmount - s.SellAmount,
            IsInstitution = s.IsInstitution
        };

    /// <summary>拉取某只股票某日的买/卖方席位明细。</summary>
    private async Task<List<DragonTigerSeat>> FetchSeatsAsync(
        HttpClient client, string code, DateTime date, string reportName, CancellationToken ct)
    {
        var url = $"{_options.DragonTigerUrl}?reportName={reportName}&columns=ALL&pageSize=20" +
                  $"&filter=(TRADE_DATE='{date:yyyy-MM-dd}')(SECURITY_CODE=%22{code}%22)";
        var json = await client.GetStringAsync(url, ct);

        var seats = new List<DragonTigerSeat>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("result", out var resultEl) ||
            resultEl.ValueKind != JsonValueKind.Object ||
            !resultEl.TryGetProperty("data", out var dataEl) ||
            dataEl.ValueKind != JsonValueKind.Array)
        {
            return seats;
        }

        foreach (var item in dataEl.EnumerateArray())
        {
            var name = Str(item, "OPERATEDEPT_NAME");
            if (string.IsNullOrEmpty(name)) continue;
            seats.Add(new DragonTigerSeat
            {
                SeatName = name,
                BuyAmount = Num(item, "BUY"),
                SellAmount = Num(item, "SELL"),
                IsInstitution = name.Contains("机构专用")
            });
        }

        return seats;
    }

    private static List<DragonTigerEntity> Parse(string json, DateTime date)
    {
        var result = new List<DragonTigerEntity>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var resultEl) ||
            resultEl.ValueKind != JsonValueKind.Object ||
            !resultEl.TryGetProperty("data", out var dataEl) ||
            dataEl.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in dataEl.EnumerateArray())
        {
            var code = Str(item, "SECURITY_CODE");
            if (string.IsNullOrEmpty(code)) continue;

            var reason = Str(item, "EXPLANATION");
            result.Add(new DragonTigerEntity
            {
                Date = date,
                Code = code,
                Name = Str(item, "SECURITY_NAME_ABBR"),
                Reason = reason,
                NetBuyAmount = Num(item, "BILLBOARD_NET_AMT"),
                BuyAmount = Num(item, "BILLBOARD_BUY_AMT"),
                SellAmount = Num(item, "BILLBOARD_SELL_AMT")
            });
        }

        // 同一股票当日可能多次上榜（不同原因）→ 按 code 去重，避免撞唯一键 (code,date)：
        // 保留净买额绝对值最大的一条，合并上榜原因。机构标记稍后由席位明细填充。
        return result
            .GroupBy(r => r.Code)
            .Select(g =>
            {
                var rep = g.OrderByDescending(x => Math.Abs(x.NetBuyAmount)).First();
                var reasons = g.Select(x => x.Reason).Where(s => !string.IsNullOrEmpty(s)).Distinct();
                rep.Reason = string.Join("；", reasons);
                if (rep.Reason.Length > 200) rep.Reason = rep.Reason[..200];
                return rep;
            })
            .ToList();
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static decimal Num(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(v.GetString(), out var d) ? d : 0,
            _ => 0
        };
    }
}
