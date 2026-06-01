using System.Text.Json;
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
        var hotConcepts = await ComputeHotConceptsAsync(latest, ct);
        await EnrichIndustryConceptsAsync(results, hotConcepts, ct);
        _logger.LogInformation("选股完成：活跃池 {Pool} 只，入选 TOP {Top}，当日热门题材 {Hot} 个",
            activePool.Count, results.Count, hotConcepts.Count);
        return results;
    }

    /// <summary>
    /// 计算当日热门题材：当日活跃股（涨停/大涨/放量）扎堆的概念，活跃股越多越热。
    /// 返回 概念→活跃股数。反映"当下市场在炒什么"。
    /// </summary>
    private async Task<Dictionary<string, int>> ComputeHotConceptsAsync(
        List<DailyMarketSnapshotEntity> latest, CancellationToken ct)
    {
        var activeCodes = latest
            .Where(s => s.IsLimitUp || s.ChangePercent >= 5m
                || (s.VolumeRatio >= 2m && s.ChangePercent > 0))
            .Select(s => s.Code)
            .ToList();
        if (activeCodes.Count == 0) return new Dictionary<string, int>();

        var rel = await _db.StockConceptRelation
            .Where(c => activeCodes.Contains(c.StockCode))
            .Select(c => new { c.StockCode, c.ConceptName })
            .ToListAsync(ct);

        return rel
            .GroupBy(c => c.ConceptName)
            .Select(g => new { Concept = g.Key, Count = g.Select(x => x.StockCode).Distinct().Count() })
            .Where(x => x.Count >= 2) // 至少 2 只活跃股才算成"题材"
            .ToDictionary(x => x.Concept, x => x.Count);
    }

    /// <summary>补充行业、概念/题材；命中热门题材的概念优先排序并标记。</summary>
    private async Task EnrichIndustryConceptsAsync(
        List<StockSelectionResult> results, Dictionary<string, int> hotConcepts, CancellationToken ct)
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
            {
                // 命中热门题材的概念优先（热度高在前），其余按名称
                r.Concepts = cs
                    .OrderByDescending(c => hotConcepts.TryGetValue(c, out var h) ? h : 0)
                    .ThenBy(c => c)
                    .ToList();
                r.HotConcepts = cs.Where(hotConcepts.ContainsKey).ToList();
            }
        }
    }

    /// <summary>
    /// 取当日已冻结的选股结果：已落库当日批次直接返回（盘中刷新结果不跳动）；
    /// 无则跑一次并落库冻结。需要重新选股请调 <see cref="RunAndSaveAsync"/>。
    /// </summary>
    public async Task<List<StockSelectionResult>> GetOrCreateLatestAsync(int topN = 5, CancellationToken ct = default)
    {
        var tradingDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        if (tradingDate == null) return new();

        var existing = await _db.SelectionResult
            .FirstOrDefaultAsync(r => r.TradingDate == tradingDate.Value, ct);
        if (existing != null)
            return Deserialize(existing.ResultsJson, topN);

        return await RunAndSaveAsync(new SelectionCriteria { TopN = topN }, ct);
    }

    /// <summary>重新选股并落库（覆盖当日批次），返回结果。供手动刷新 / 收盘后任务调用。</summary>
    public async Task<List<StockSelectionResult>> RunAndSaveAsync(SelectionCriteria criteria, CancellationToken ct = default)
    {
        var results = await SelectAsync(criteria, ct);

        var tradingDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        if (tradingDate == null) return results;

        var json = JsonSerializer.Serialize(results);
        var row = await _db.SelectionResult.FirstOrDefaultAsync(r => r.TradingDate == tradingDate.Value, ct);
        if (row == null)
        {
            _db.SelectionResult.Add(new SelectionResultEntity
            {
                TradingDate = tradingDate.Value,
                RunAt = DateTime.UtcNow,
                TopN = criteria.TopN,
                ResultsJson = json,
            });
        }
        else
        {
            row.RunAt = DateTime.UtcNow;
            row.TopN = criteria.TopN;
            row.ResultsJson = json;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("选股结果已冻结落库：{Date} TOP{Top}", tradingDate.Value.ToString("yyyy-MM-dd"), results.Count);
        return results;
    }

    private static List<StockSelectionResult> Deserialize(string json, int topN)
    {
        var list = JsonSerializer.Deserialize<List<StockSelectionResult>>(json) ?? new();
        return topN > 0 ? list.Take(topN).ToList() : list;
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
