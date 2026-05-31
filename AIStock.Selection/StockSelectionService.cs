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
        var (snapshots, dragonByCode) = await LoadLatestAsync(ct);
        if (snapshots.Count == 0)
        {
            _logger.LogWarning("daily_market_snapshot 无数据，选股返回空。请先运行 market-snapshot 采集任务。");
            return new List<StockSelectionResult>();
        }

        var activePool = ActivityScreener.Screen(snapshots, criteria);
        var results = _engine.Select(activePool, dragonByCode, criteria);
        _logger.LogInformation("选股完成：活跃池 {Pool} 只，入选 TOP {Top}", activePool.Count, results.Count);
        return results;
    }

    /// <summary>仅返回第一级活跃度粗筛池（调试/观察用）。</summary>
    public async Task<List<ActivityScreener.ActivityHit>> ScreenActivityAsync(SelectionCriteria criteria, CancellationToken ct = default)
    {
        var (snapshots, _) = await LoadLatestAsync(ct);
        return ActivityScreener.Screen(snapshots, criteria);
    }

    private async Task<(List<DailyMarketSnapshotEntity> snapshots, Dictionary<string, DragonTigerEntity> dragonByCode)> LoadLatestAsync(CancellationToken ct)
    {
        var latestDate = await _db.DailyMarketSnapshot
            .MaxAsync(s => (DateTime?)s.Date, ct);

        if (latestDate == null)
            return (new List<DailyMarketSnapshotEntity>(), new Dictionary<string, DragonTigerEntity>());

        var snapshots = await _db.DailyMarketSnapshot
            .Where(s => s.Date == latestDate)
            .ToListAsync(ct);

        var dragons = await _db.DragonTiger
            .Where(d => d.Date == latestDate)
            .ToListAsync(ct);

        var dragonByCode = dragons
            .GroupBy(d => d.Code)
            .ToDictionary(g => g.Key, g => g.First());

        return (snapshots, dragonByCode);
    }
}
