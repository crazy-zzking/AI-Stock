using System.Text.Json;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Services;

/// <summary>
/// 龙虎榜采集服务 — 拉取东财当日龙虎榜明细，按 (code, date) upsert 到 dragon_tiger。
/// 东财 datacenter 字段以实盘联调为准，解析采用安全取值，缺字段不阻断。
/// </summary>
public class DragonTigerSyncService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MarketSnapshotOptions _options;
    private readonly ILogger<DragonTigerSyncService> _logger;

    public DragonTigerSyncService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<MarketSnapshotOptions> options,
        ILogger<DragonTigerSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>采集指定交易日（默认今天）的龙虎榜，返回落库条数。</summary>
    public async Task<int> SyncAsync(DateTime? tradeDate = null, CancellationToken ct = default)
    {
        var date = (tradeDate ?? DateTime.Now).Date;
        var url = $"{_options.DragonTigerUrl}?reportName=RPT_DAILYBILLBOARD_DETAILSNEW" +
                  $"&columns=ALL&pageSize=1000&pageNumber=1&sortColumns=BILLBOARD_NET_AMT&sortTypes=-1" +
                  $"&filter=(TRADE_DATE='{date:yyyy-MM-dd}')";

        string json;
        try
        {
            var client = _httpClientFactory.CreateClient("default");
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
            }
            ok++;
        }
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("龙虎榜采集完成：{Ok} 条（{Date}）", ok, date.ToString("yyyy-MM-dd"));
        return ok;
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
                SellAmount = Num(item, "BILLBOARD_SELL_AMT"),
                // 机构席位需明细接口，暂以原因含"机构"近似；联调时以席位明细为准
                HasInstitution = reason.Contains("机构")
            });
        }

        return result;
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
