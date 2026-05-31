using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection;

/// <summary>
/// 选股编排服务 — 从每日快照表（+龙虎榜）取最新交易日数据，
/// 跑两级漏斗（活跃度粗筛 → 多因子打分），返回 TOP-N。
/// </summary>
public class StockSelectionService
{
    private readonly AIStockDbContext _db;
    private readonly StockSelectionEngine _engine;
    private readonly ILogger<StockSelectionService> _logger;

    public StockSelectionService(
        AIStockDbContext db,
        StockSelectionEngine engine,
        ILogger<StockSelectionService> logger)
    {
        _db = db;
        _engine = engine;
        _logger = logger;
    }

    /// <summary>执行选股，返回 TOP-N。无快照数据时返回空。</summary>
    public async Task<List<StockSelectionResult>> SelectAsync(SelectionCriteria criteria, CancellationToken ct = default)
    {
        var (latest, dragonByCode, sequenceByCode) = await LoadAsync(ct);
        if (latest.Count == 0)
        {
            _logger.LogWarning("daily_market_snapshot 无数据，选股返回空。请先运行 market-snapshot 采集任务。");
            return new List<StockSelectionResult>();
        }

        var activePool = ActivityScreener.Screen(latest, criteria);
        var results = _engine.Select(activePool, dragonByCode, sequenceByCode, criteria);
        await EnrichIndustryConceptsAsync(results, ct);
        _logger.LogInformation("选股完成：活跃池 {Pool} 只，入选 TOP {Top}", activePool.Count, results.Count);
        return results;
    }

    /// <summary>给选股结果补充行业（stock_base）与关联概念/题材（stock_concept_relation）。</summary>
    private async Task EnrichIndustryConceptsAsync(List<StockSelectionResult> results, CancellationToken ct)
    {
        if (results.Count == 0) return;
        var codes = results.Select(r => r.Code).ToList();

        var industries = await _db.StockBase
            .Where(s => codes.Contains(s.Code))
            .Select(s => new { s.Code, s.Industry })
            .ToDictionaryAsync(x => x.Code, x => x.Industry, ct);

        var concepts = (await _db.StockConceptRelation
            .Where(c => codes.Contains(c.StockCode))
            .Select(c => new { c.StockCode, c.ConceptName })
            .ToListAsync(ct))
            .GroupBy(c => c.StockCode)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConceptName).Distinct().ToList());

        foreach (var r in results)
        {
            if (industries.TryGetValue(r.Code, out var ind) && !string.IsNullOrEmpty(ind))
                r.Industry = ind;
            if (concepts.TryGetValue(r.Code, out var cs))
                r.Concepts = cs;
        }
    }

    /// <summary>仅返回第一级活跃度粗筛池（调试/观察用）。</summary>
    public async Task<List<ActivityScreener.ActivityHit>> ScreenActivityAsync(SelectionCriteria criteria, CancellationToken ct = default)
    {
        var (latest, _, _) = await LoadAsync(ct);
        return ActivityScreener.Screen(latest, criteria);
    }

    /// <summary>
    /// 取最近约 40 自然日（覆盖 ~20 交易日）快照：当日行用于粗筛/展示，
    /// 整段按 code 组装序列算多日特征，龙虎榜取最新日。
    /// </summary>
    private async Task<(List<DailyMarketSnapshotEntity> latest,
        Dictionary<string, DragonTigerEntity> dragonByCode,
        Dictionary<string, SequenceFeatures> sequenceByCode)> LoadAsync(CancellationToken ct)
    {
        var latestDate = await _db.DailyMarketSnapshot
            .MaxAsync(s => (DateTime?)s.Date, ct);

        if (latestDate == null)
            return (new(), new(), new());

        var since = latestDate.Value.AddDays(-40);
        var rows = await _db.DailyMarketSnapshot
            .Where(s => s.Date >= since && s.Date <= latestDate)
            .ToListAsync(ct);

        var sequenceByCode = rows
            .GroupBy(r => r.Code)
            .ToDictionary(
                g => g.Key,
                g => SequenceAnalyzer.Analyze(g.OrderBy(x => x.Date).ToList()));

        var latest = rows.Where(r => r.Date == latestDate.Value).ToList();

        var dragons = await _db.DragonTiger
            .Where(d => d.Date == latestDate.Value)
            .ToListAsync(ct);
        var dragonByCode = dragons
            .GroupBy(d => d.Code)
            .ToDictionary(g => g.Key, g => g.First());

        return (latest, dragonByCode, sequenceByCode);
    }
}
