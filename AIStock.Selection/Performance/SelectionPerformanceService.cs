using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Json;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection.Performance;

/// <summary>
/// 选股信号前向绩效服务：
/// 1) 物化 — 把 selection_result 中每 (交易日, 策略) 的最新一批选股固化为 selection_performance 信号行（既有行不删除）；
/// 2) 补算 — 随日K到位计算 T+1/T+3/T+5 收益与沪深300超额，T+5 齐了（或确认一字板买不进）标记 final；
/// 3) 聚合 — 按策略汇总胜率/均值收益/超额/盈亏比，供"策略记分板"。
/// </summary>
public class SelectionPerformanceService
{
    /// <summary>基准指数：沪深300（东财 secid，IndexKlineSyncService 同步进 kline_data）。</summary>
    public const string BenchmarkSecid = "1.000300";

    /// <summary>物化回看窗口（天）：只处理最近 N 天的选股批次，避免全表扫描。</summary>
    private const int MaterializeLookbackDays = 60;

    private readonly AIStockDbContext _db;
    private readonly ILogger<SelectionPerformanceService> _logger;

    public SelectionPerformanceService(AIStockDbContext db, ILogger<SelectionPerformanceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>物化新信号 + 补算未完成行。返回（新增信号数, 更新行数, 本轮转 final 数）。</summary>
    public async Task<(int Created, int Updated, int Finalized)> SyncAsync(CancellationToken ct = default)
    {
        var created = await MaterializeAsync(ct);
        var (updated, finalized) = await UpdateReturnsAsync(ct);
        _logger.LogInformation("选股绩效同步完成：新增信号 {Created}，更新 {Updated}，完成 {Finalized}",
            created, updated, finalized);
        return (created, updated, finalized);
    }

    /// <summary>把每 (交易日, 策略) run_at 最新一批选股固化为信号行；已存在的 (交易日,策略,代码) 跳过。</summary>
    private async Task<int> MaterializeAsync(CancellationToken ct)
    {
        var since = DateTime.Today.AddDays(-MaterializeLookbackDays);

        var batches = await _db.SelectionResult
            .Where(r => r.TradingDate >= since)
            .Select(r => new { r.Id, r.TradingDate, r.RunAt, r.Strategy, r.StrategyName, r.ResultsJson })
            .ToListAsync(ct);
        if (batches.Count == 0) return 0;

        // 每 (交易日, 策略) 取 run_at 最新一批为当日正式信号
        var canonical = batches
            .GroupBy(b => (Date: b.TradingDate.Date, b.Strategy))
            .Select(g => g.OrderByDescending(b => b.RunAt).First())
            .ToList();

        var existing = (await _db.SelectionPerformance
                .Where(p => p.TradingDate >= since)
                .Select(p => new { p.TradingDate, p.Strategy, p.Code })
                .ToListAsync(ct))
            .Select(x => (x.TradingDate.Date, x.Strategy, x.Code))
            .ToHashSet();

        var created = 0;
        foreach (var batch in canonical)
        {
            List<StockSelectionResult> picks;
            try
            {
                picks = JsonSerializer.Deserialize<List<StockSelectionResult>>(batch.ResultsJson, AppJson.Default) ?? new();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "选股绩效：批次 {Id} results_json 解析失败，跳过", batch.Id);
                continue;
            }

            foreach (var p in picks)
            {
                if (string.IsNullOrEmpty(p.Code)) continue;
                if (!existing.Add((batch.TradingDate.Date, batch.Strategy, p.Code))) continue;

                _db.SelectionPerformance.Add(new SelectionPerformanceEntity
                {
                    TradingDate = batch.TradingDate.Date,
                    Strategy = batch.Strategy,
                    StrategyName = batch.StrategyName,
                    Code = p.Code,
                    Name = p.Name,
                    SelectionResultId = batch.Id,
                    Score = p.TotalScore,
                    SignalClose = p.Close,
                    Status = PerformanceStatus.Pending,
                });
                created++;
            }
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>对 status != final 的行用库内日K补算收益，K线到位多少算多少。</summary>
    private async Task<(int Updated, int Finalized)> UpdateReturnsAsync(CancellationToken ct)
    {
        var pending = await _db.SelectionPerformance
            .Where(p => p.Status != PerformanceStatus.Final)
            .ToListAsync(ct);
        if (pending.Count == 0) return (0, 0);

        const string daily = nameof(KlineInterval.Daily);
        var minDate = pending.Min(p => p.TradingDate).AddDays(-10); // 留缓冲刷新"昨收"（停牌跨日场景）

        var indexBars = (await _db.KlineData
                .Where(k => k.Code == BenchmarkSecid && k.Interval == daily && k.DateTime >= minDate)
                .Select(k => new { k.DateTime, k.Open, k.Close })
                .ToListAsync(ct))
            .GroupBy(k => k.DateTime.Date)
            .ToDictionary(g => g.Key, g => new PerfBar(g.Key, g.First().Open, g.First().Close));

        var codes = pending.Select(p => p.Code).Distinct().ToList();
        var stockBars = (await _db.KlineData
                .Where(k => codes.Contains(k.Code) && k.Interval == daily && k.DateTime >= minDate)
                .Select(k => new { k.Code, k.DateTime, k.Open, k.Close })
                .ToListAsync(ct))
            .GroupBy(k => k.Code)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PerfBar>)g.OrderBy(k => k.DateTime)
                    .Select(k => new PerfBar(k.DateTime.Date, k.Open, k.Close))
                    .ToList());

        int updated = 0, finalized = 0;
        foreach (var row in pending)
        {
            if (!stockBars.TryGetValue(row.Code, out var bars)) continue;

            var perf = PerformanceCalculator.Compute(
                row.TradingDate, row.SignalClose, row.Code, row.Name, bars, indexBars);
            if (perf.EntryDate == null && !perf.Untradable) continue; // K线未到位

            row.EntryDate = perf.EntryDate;
            row.EntryPrice = perf.EntryPrice;
            row.Untradable = perf.Untradable;
            row.Ret1 = perf.Ret1;
            row.Ret3 = perf.Ret3;
            row.Ret5 = perf.Ret5;
            row.Excess1 = perf.Excess1;
            row.Excess3 = perf.Excess3;
            row.Excess5 = perf.Excess5;
            row.Status = perf.Final ? PerformanceStatus.Final : PerformanceStatus.Partial;

            updated++;
            if (perf.Final) finalized++;
        }

        if (updated > 0) await _db.SaveChangesAsync(ct);
        return (updated, finalized);
    }

    /// <summary>
    /// LLM 复评价值考核：把已复评批次中每只票的复评建议（Buy/Watch/Avoid）与前向绩效 join，
    /// 按建议等级聚合 T+1/3/5 表现——回答"复评是否真的带来超额判断力"。
    /// </summary>
    public async Task<List<ReviewPerformanceSummary>> GetReviewStatsAsync(int days = 30, CancellationToken ct = default)
    {
        var since = DateTime.Today.AddDays(-days);

        var batches = await _db.SelectionResult
            .Where(r => r.TradingDate >= since && r.ReviewStatus == "done")
            .Select(r => new { r.TradingDate, r.Strategy, r.ResultsJson })
            .ToListAsync(ct);

        var perfByKey = (await _db.SelectionPerformance
                .Where(p => p.TradingDate >= since && !p.Untradable)
                .ToListAsync(ct))
            .GroupBy(p => (p.TradingDate.Date, p.Strategy, p.Code))
            .ToDictionary(g => g.Key, g => g.First());

        // 同 (日,策略,代码) 多批复评取最后一次解析到的建议
        var byRec = new Dictionary<string, List<SelectionPerformanceEntity>>();
        var reviewedTotal = 0;
        foreach (var b in batches)
        {
            List<StockSelectionResult> picks;
            try { picks = JsonSerializer.Deserialize<List<StockSelectionResult>>(b.ResultsJson, AppJson.Default) ?? new(); }
            catch (JsonException) { continue; }

            foreach (var p in picks)
            {
                if (p.Review == null || string.IsNullOrEmpty(p.Code)) continue;
                reviewedTotal++;
                if (!perfByKey.TryGetValue((b.TradingDate.Date, b.Strategy, p.Code), out var perf)) continue;
                var rec = p.Review.Recommendation.ToString();
                if (!byRec.TryGetValue(rec, out var list)) byRec[rec] = list = new();
                list.Add(perf);
            }
        }

        _logger.LogInformation("复评考核：窗口 {Days} 天，已复评票次 {Total}，可匹配绩效 {Matched}",
            days, reviewedTotal, byRec.Values.Sum(v => v.Count));

        return byRec
            .Select(kv => new ReviewPerformanceSummary
            {
                Recommendation = kv.Key,
                Count = kv.Value.Count,
                Horizon1 = HorizonStats.From(kv.Value, p => p.Ret1, p => p.Excess1),
                Horizon3 = HorizonStats.From(kv.Value, p => p.Ret3, p => p.Excess3),
                Horizon5 = HorizonStats.From(kv.Value, p => p.Ret5, p => p.Excess5),
            })
            .OrderBy(s => s.Recommendation)
            .ToList();
    }

    /// <summary>按策略聚合最近 N 天信号的绩效（策略记分板）。</summary>
    public async Task<List<StrategyPerformanceSummary>> GetSummaryAsync(int days = 30, CancellationToken ct = default)
    {
        var since = DateTime.Today.AddDays(-days);
        var rows = await _db.SelectionPerformance
            .Where(p => p.TradingDate >= since)
            .ToListAsync(ct);

        return rows
            .GroupBy(p => p.Strategy)
            .Select(g =>
            {
                var tradable = g.Where(p => !p.Untradable).ToList();
                return new StrategyPerformanceSummary
                {
                    Strategy = g.Key,
                    // 早期批次未记录策略键 → 显示"(未记录)"，避免前端空白行
                    StrategyName = g.Select(p => p.StrategyName).LastOrDefault(n => !string.IsNullOrEmpty(n))
                        ?? (string.IsNullOrEmpty(g.Key) ? "(未记录)" : g.Key),
                    Signals = g.Count(),
                    Untradable = g.Count(p => p.Untradable),
                    Pending = g.Count(p => p.Status == PerformanceStatus.Pending),
                    Horizon1 = HorizonStats.From(tradable, p => p.Ret1, p => p.Excess1),
                    Horizon3 = HorizonStats.From(tradable, p => p.Ret3, p => p.Excess3),
                    Horizon5 = HorizonStats.From(tradable, p => p.Ret5, p => p.Excess5),
                };
            })
            .OrderByDescending(s => s.Horizon5.AvgExcess ?? decimal.MinValue)
            .ToList();
    }
}

/// <summary>LLM 复评考核 — 按建议等级（Buy/Watch/Avoid）聚合的前向绩效。</summary>
public class ReviewPerformanceSummary
{
    /// <summary>复评建议等级（Buy/Watch/Avoid）</summary>
    public string Recommendation { get; set; } = string.Empty;

    /// <summary>可匹配到绩效的票次</summary>
    public int Count { get; set; }

    public HorizonStats Horizon1 { get; set; } = new();
    public HorizonStats Horizon3 { get; set; } = new();
    public HorizonStats Horizon5 { get; set; } = new();
}

/// <summary>策略记分板 — 单策略聚合。</summary>
public class StrategyPerformanceSummary
{
    public string Strategy { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>信号总数（含未完成/不可成交）</summary>
    public int Signals { get; set; }

    /// <summary>一字板买不进的信号数</summary>
    public int Untradable { get; set; }

    /// <summary>尚无任何K线的信号数</summary>
    public int Pending { get; set; }

    public HorizonStats Horizon1 { get; set; } = new();
    public HorizonStats Horizon3 { get; set; } = new();
    public HorizonStats Horizon5 { get; set; } = new();
}

/// <summary>某持有窗口（T+1/T+3/T+5）的聚合统计。仅统计可成交且该窗口已有数据的信号。</summary>
public class HorizonStats
{
    /// <summary>样本数</summary>
    public int Count { get; set; }

    /// <summary>胜率（收益&gt;0 占比，%）</summary>
    public decimal? WinRate { get; set; }

    /// <summary>平均收益（%）</summary>
    public decimal? AvgRet { get; set; }

    /// <summary>平均超额（百分点，vs 沪深300）</summary>
    public decimal? AvgExcess { get; set; }

    /// <summary>盈亏比 = 总盈利 / 总亏损（无亏损时为 null）</summary>
    public decimal? ProfitFactor { get; set; }

    public static HorizonStats From(
        IReadOnlyList<SelectionPerformanceEntity> rows,
        Func<SelectionPerformanceEntity, decimal?> ret,
        Func<SelectionPerformanceEntity, decimal?> excess)
    {
        var rets = rows.Select(ret).Where(r => r != null).Select(r => r!.Value).ToList();
        if (rets.Count == 0) return new HorizonStats();

        var excesses = rows.Select(excess).Where(e => e != null).Select(e => e!.Value).ToList();
        var gain = rets.Where(r => r > 0).Sum();
        var loss = Math.Abs(rets.Where(r => r < 0).Sum());

        return new HorizonStats
        {
            Count = rets.Count,
            WinRate = Math.Round((decimal)rets.Count(r => r > 0) / rets.Count * 100, 1),
            AvgRet = Math.Round(rets.Average(), 2),
            AvgExcess = excesses.Count > 0 ? Math.Round(excesses.Average(), 2) : null,
            ProfitFactor = loss > 0 ? Math.Round(gain / loss, 2) : null,
        };
    }
}
