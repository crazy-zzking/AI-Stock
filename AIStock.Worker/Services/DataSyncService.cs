using System.Text.Json;
using System.Text.Json.Serialization;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Services;

/// <summary>
/// 数据同步服务 — 将股票池与日K落库到 stock_base / kline_data。
/// 股票池来自 DataSync:StockCodesUrl 接口，K线来自数据源 Provider。
/// </summary>
public partial class DataSyncService
{
    private readonly IDataProviderResolver _resolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITradingCalendar _tradingCalendar;
    private readonly DataSyncOptions _options;
    private readonly ILogger<DataSyncService> _logger;

    public DataSyncService(
        IDataProviderResolver resolver,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ITradingCalendar tradingCalendar,
        IOptions<DataSyncOptions> options,
        ILogger<DataSyncService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _tradingCalendar = tradingCalendar;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 从股票池接口获取全市场代码。
    /// </summary>
    private async Task<List<StockCodeDto>> FetchStockUniverseAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("default");
        var json = await client.GetStringAsync(_options.StockCodesUrl, ct);

        var resp = JsonSerializer.Deserialize<StockCodesResponse>(json);
        if (resp == null || resp.Code != 0 || resp.Data?.Codes == null)
        {
            _logger.LogWarning("股票池接口返回异常: code={Code}, message={Message}", resp?.Code, resp?.Message);
            return new List<StockCodeDto>();
        }

        return resp.Data.Codes;
    }

    /// <summary>
    /// 同步股票池基础信息到 stock_base（按代码 upsert）。返回 upsert 的股票数。
    /// </summary>
    public async Task<int> SyncStockBaseAsync(CancellationToken ct = default)
    {
        var stocks = await FetchStockUniverseAsync(ct);
        if (stocks.Count == 0)
        {
            _logger.LogWarning("股票池为空，跳过 stock_base 同步");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var existing = await db.StockBase.ToDictionaryAsync(s => s.Code, ct);
        var now = DateTime.Now;
        var upserted = 0;

        foreach (var s in stocks)
        {
            if (string.IsNullOrEmpty(s.Code)) continue;

            var market = s.Exchange?.ToUpperInvariant() ?? string.Empty;

            if (existing.TryGetValue(s.Code, out var entity))
            {
                entity.Name = s.Name ?? entity.Name;
                entity.Market = market;
                entity.UpdatedAt = now;
            }
            else
            {
                db.StockBase.Add(new StockBaseEntity
                {
                    Code = s.Code,
                    Name = s.Name ?? string.Empty,
                    Market = market,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            upserted++;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("stock_base 同步完成：{Count} 只股票", upserted);
        return upserted;
    }

    /// <summary>
    /// 同步日K到 kline_data。仅插入比库内最新日期更新的K线，避免重复。
    /// </summary>
    public async Task<int> SyncKlinesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        // 目标交易日 = 最近"已收盘"交易日（今日未过收盘时刻则取上一交易日）
        var target = await GetLastClosedTradingDayAsync(_options.SyncHour, ct);

        // 只取"未同步到目标交易日"的股票（已同步到 target 的直接跳过，不再每次全量拉取）
        var codesQuery = db.StockBase
            .Where(s => !s.IsDelisted && (s.LastKlineSyncDate == null || s.LastKlineSyncDate < target.Date))
            .OrderBy(s => s.Code).Select(s => s.Code);
        if (_options.MaxStocks > 0)
            codesQuery = codesQuery.Take(_options.MaxStocks);
        var codes = await codesQuery.ToListAsync(ct);

        if (codes.Count == 0)
        {
            _logger.LogInformation("K线已全部同步到 {Target}，无需拉取", target.ToString("yyyy-MM-dd"));
            return 0;
        }

        // K线统一用东方财富抓取
        var provider = _resolver.GetProviders(DataCapability.Kline)
            .FirstOrDefault(p => p.ProviderName == "东方财富");
        if (provider == null)
        {
            _logger.LogWarning("未找到东方财富数据源，跳过K线同步");
            return 0;
        }

        const string interval = nameof(KlineInterval.Daily);
        var totalInserted = 0;
        var processed = 0;

        // 一次性取出各股票库内最新日K日期，用于增量去重（DbContext 非线程安全，不能在并发任务里查库）
        var klineMaxDates = await db.KlineData
            .Where(k => k.Interval == interval)
            .GroupBy(k => k.Code)
            .Select(g => new { Code = g.Key, Last = g.Max(x => x.DateTime) })
            .ToDictionaryAsync(x => x.Code, x => x.Last, ct);

        var batchSize = Math.Max(1, _options.KlineBatchSize);
        _logger.LogInformation("开始K线同步：{Count} 只待同步股票，目标交易日 {Target}（批大小 {Batch}）",
            codes.Count, target.ToString("yyyy-MM-dd"), batchSize);

        for (int i = 0; i < codes.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;
            var batch = codes.Skip(i).Take(batchSize).ToList();

            // 本批 stock_base 实体（用于读取/回写 LastKlineSyncDate，受 DbContext 跟踪）
            var stockEnts = await db.StockBase
                .Where(s => batch.Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, s => s, ct);

            // 批内并发拉取（仅 HTTP + 读只读字典，不碰 DbContext）
            var tasks = batch.Select(async code =>
            {
                // 最后同步日期：优先用 stock_base 记录，其次回退库内最新日K日期（首次迁移后兼容）
                stockEnts.TryGetValue(code, out var ent);
                DateTime? klineMax = klineMaxDates.TryGetValue(code, out var d) ? d : null;
                DateTime? lastSync = ent?.LastKlineSyncDate ?? klineMax;
                var lmt = ComputeKlineFetchCount(lastSync, target);
                try
                {
                    if (lmt == 0) return (code, null);
                    var klines = await provider.GetKlinesAsync(code, KlineInterval.Daily, lmt);
                    return (code, klines);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "K线拉取失败 {Code}", code);
                    return (code, (List<AIStock.Core.Models.KlineData>?)null);
                }
            });
            var results = await Task.WhenAll(tasks);

            // DB 写入串行：按库内最新日期去重后落库，并回写 LastKlineSyncDate
            foreach (var (code, klines) in results)
            {
                if (klines == null || klines.Count == 0) continue;

                // 截断到目标交易日：剔除未收盘当日的成形中K线，避免落入不完整数据
                var capped = klines.Where(k => k.DateTime.Date <= target.Date).ToList();
                if (capped.Count == 0) continue;

                var hasLast = klineMaxDates.TryGetValue(code, out var lastDate);
                var fresh = capped
                    .Where(k => !hasLast || k.DateTime > lastDate)
                    .Select(k => new KlineDataEntity
                    {
                        Code = code,
                        DateTime = k.DateTime,
                        Interval = interval,
                        Open = k.Open,
                        Close = k.Close,
                        High = k.High,
                        Low = k.Low,
                        Volume = k.Volume,
                        Amount = k.Amount,
                        TurnoverRate = k.TurnoverRate,
                        ChangePercent = k.ChangePercent,
                        Source = provider.ProviderName,
                        CreatedAt = DateTime.Now
                    })
                    .ToList();

                if (fresh.Count > 0)
                {
                    db.KlineData.AddRange(fresh);
                    totalInserted += fresh.Count;
                }

                // 最后同步日期 = 最后一根K线的日期（截断到目标日内），非同步执行时间
                if (stockEnts.TryGetValue(code, out var stock))
                    stock.LastKlineSyncDate = capped.Max(k => k.DateTime);
            }
            await db.SaveChangesAsync(ct);
            processed += batch.Count;

            if (processed % 50 == 0 || i + batchSize >= codes.Count)
                _logger.LogInformation("K线同步进度：{Processed}/{Total}，累计新增 {Rows} 条", processed, codes.Count, totalInserted);

            // 批间节流（批大小<=1 时退化为逐只节流，沿用 KlineThrottleMs）
            var delay = batchSize > 1 ? _options.KlineBatchDelayMs : _options.KlineThrottleMs;
            if (delay > 0 && i + batchSize < codes.Count)
                await Task.Delay(delay, ct);
        }

        _logger.LogInformation("kline_data 同步完成：{Stocks} 只股票，新增 {Rows} 条K线", codes.Count, totalInserted);
        return totalInserted;
    }

    /// <summary>
    /// 据"最后同步日期"决定本次拉取条数：
    /// 从未同步(null) → 拉满 KlineCount（取深度历史）；
    /// 否则按落后自然日折算交易日(×5/7) + 缓冲，区间 [2, KlineCount]。
    /// 既能让日常增量只拉最近几根（快），又能在断更/缺口时一次补齐。
    /// </summary>
    private int ComputeKlineFetchCount(DateTime? lastSync, DateTime target)
    {
        var max = Math.Max(2, _options.KlineCount);
        if (lastSync == null) return max; // 首次/无历史：按配置深度拉满

        var daysBehind = (target.Date - lastSync.Value.Date).TotalDays;
        if (daysBehind <= 0) return 0; // 已同步到目标交易日，无需拉取

        var approxTradingDays = (int)Math.Ceiling(daysBehind * 5.0 / 7.0);
        return Math.Clamp(approxTradingDays + 5, 2, max); // +5 缓冲，封顶 KlineCount
    }

    /// <summary>库内最新日K日期（无数据返回 null）</summary>
    public async Task<DateTime?> GetLastKlineDateAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var interval = nameof(KlineInterval.Daily);
        return await db.KlineData
            .Where(k => k.Interval == interval)
            .MaxAsync(k => (DateTime?)k.DateTime, ct);
    }

    /// <summary>
    /// 最近一个"已收盘"的交易日：今天若是交易日且已过收盘时刻则取今天，否则向前找最近交易日。
    /// </summary>
    public async Task<DateTime> GetLastClosedTradingDayAsync(int closeHour, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var day = now.Date;
        if (now.Hour < closeHour) day = day.AddDays(-1); // 今日尚未收盘，从昨天起找

        // 拉取一段窗口的交易日集合（接口判断），再回溯找最近交易日
        var tradingDays = await _tradingCalendar.FetchTradingDaysAsync(day.AddDays(-25), now.Date, ct);

        for (int i = 0; i < 25; i++)
        {
            bool isTradingDay = tradingDays.Count > 0
                ? tradingDays.Contains(day)
                : day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday; // 接口不可用时降级为周末判断
            if (isTradingDay) return day;
            day = day.AddDays(-1);
        }
        return day;
    }

    // FetchTradingDaysAsync 已抽取为共享 ITradingCalendar 服务

    /// <summary>
    /// 是否需要同步K线：只要还有股票未同步到最近已收盘交易日（含从未同步），就需要。
    /// 按只判断（stock_base.last_kline_sync_date），避免"个别股票已到今天 → 全局最大日期达标 → 整体跳过、其余股票漏同步"。
    /// </summary>
    public async Task<(bool Need, DateTime? Last, DateTime Target)> ShouldSyncKlinesAsync(int closeHour, CancellationToken ct = default)
    {
        var target = await GetLastClosedTradingDayAsync(closeHour, ct);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        // 仍有未同步到目标交易日的股票（从未同步或落后）→ 需要继续同步
        var pending = await db.StockBase
            .CountAsync(s => !s.IsDelisted &&
                (s.LastKlineSyncDate == null || s.LastKlineSyncDate < target.Date), ct);

        // 库内最新日K日期，仅用于日志展示
        var last = await db.KlineData
            .Where(k => k.Interval == nameof(KlineInterval.Daily))
            .MaxAsync(k => (DateTime?)k.DateTime, ct);

        return (pending > 0, last, target);
    }
}

/// <summary>股票池接口响应</summary>
public class StockCodesResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public StockCodesData? Data { get; set; }
}

public class StockCodesData
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("codes")] public List<StockCodeDto>? Codes { get; set; }
}

public class StockCodeDto
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("exchange")] public string? Exchange { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

// WorkdayResponse / WorkdayData / WorkdayItem 已移至 AIStock.Data.Services.TradingCalendarService
