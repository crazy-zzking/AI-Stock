using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 参数回放回测：用给定参数(SelectionCriteria)+策略，在历史快照上逐交易日重跑选股，
/// 汇总信号后用统一回测引擎评估。这是"换参数看回测效果"的能力，供大模型自动调参优化。
/// 大盘环境用无指数简化版（不调外部接口，见 SelectionContextBuilder）。
/// </summary>
public class ReplayBacktestService
{
    private readonly AIStockDbContext _db;
    private readonly IReadOnlyDictionary<string, ISelectionStrategy> _strategies;
    private readonly ILogger<ReplayBacktestService> _logger;

    public ReplayBacktestService(
        AIStockDbContext db, IEnumerable<ISelectionStrategy> strategies, ILogger<ReplayBacktestService> logger)
    {
        _db = db;
        _strategies = strategies.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    private ISelectionStrategy Resolve(string? key)
        => !string.IsNullOrWhiteSpace(key) && _strategies.TryGetValue(key.Trim(), out var s)
            ? s : _strategies[StrategyKeys.LowDip];

    /// <summary>
    /// 回放回测：[from,to] 内每个交易日用 criteria 跑策略选股 → 信号 → 回测。
    /// </summary>
    public async Task<BacktestReport> BacktestParamsAsync(
        string? strategyKey, SelectionCriteria criteria, DateTime from, DateTime to,
        BacktestConfig config, CancellationToken ct = default)
    {
        var strategy = Resolve(strategyKey);
        const int seqWindow = 45; // 多日序列回看自然日

        // 批量预载：[from-缓冲, to] 全部快照（缓冲供序列特征回看）
        var since = from.Date.AddDays(-(seqWindow + 15));
        var allShots = await _db.DailyMarketSnapshot
            .Where(s => s.Date >= since && s.Date <= to.Date)
            .ToListAsync(ct);
        if (allShots.Count == 0)
        {
            _logger.LogWarning("回放回测：区间内无快照数据");
            return new BacktestReport { HoldDays = config.HoldDays, Entry = config.Entry.ToString() };
        }

        var shotsByCode = allShots.GroupBy(s => s.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList());
        var shotsByDate = allShots.GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DailyMarketSnapshotEntity>)g.ToList());
        var tradingDays = shotsByDate.Keys.Where(d => d >= from.Date && d <= to.Date).OrderBy(d => d).ToList();

        var allCodes = shotsByCode.Keys.ToList();
        var conceptsByCode = (await _db.StockConceptRelation
                .Where(c => allCodes.Contains(c.StockCode))
                .Select(c => new { c.StockCode, c.ConceptName })
                .ToListAsync(ct))
            .GroupBy(c => c.StockCode)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConceptName).Distinct().ToList());
        var industryByCode = await _db.StockBase
            .Where(s => allCodes.Contains(s.Code) && s.Industry != null && s.Industry != "")
            .Select(s => new { s.Code, s.Industry })
            .ToDictionaryAsync(x => x.Code, x => x.Industry!, ct);

        var dragonsByDate = (await _db.DragonTiger
                .Where(d => d.Date >= from.Date && d.Date <= to.Date)
                .ToListAsync(ct))
            .GroupBy(d => d.Date.Date)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, DragonTigerEntity>)g.GroupBy(x => x.Code)
                        .ToDictionary(x => x.Key, x => x.First()));

        var emptyDragons = new Dictionary<string, DragonTigerEntity>();
        var signals = new List<BacktestSignal>();

        foreach (var day in tradingDays)
        {
            var dayShots = shotsByDate[day];

            var context = new SelectionContext
            {
                Regime = SelectionContextBuilder.BuildRegimeFromBreadth(dayShots),
                HotConcepts = SelectionContextBuilder.ComputeHotConcepts(dayShots, conceptsByCode),
                ConceptsByCode = conceptsByCode,
                IndustryByCode = industryByCode,
                IndustryStrength = SelectionContextBuilder.ComputeSectorStrength(dayShots, industryByCode),
            };

            var activePool = ActivityScreener.Screen(dayShots.ToList(), criteria);

            // 仅对活跃池股票算"截至当日"的多日序列特征
            var seqByCode = new Dictionary<string, SequenceFeatures>();
            foreach (var hit in activePool)
            {
                if (shotsByCode.TryGetValue(hit.Snapshot.Code, out var series))
                {
                    var window = series.Where(s => s.Date <= day).TakeLast(seqWindow).ToList();
                    seqByCode[hit.Snapshot.Code] = SequenceAnalyzer.Analyze(window);
                }
            }

            var dragons = dragonsByDate.TryGetValue(day, out var d) ? d : emptyDragons;
            var picks = strategy.Select(activePool, dragons, seqByCode, criteria, context);

            foreach (var p in picks)
                signals.Add(new BacktestSignal { Date = day, Code = p.Code, Name = p.Name });
        }

        if (signals.Count == 0)
        {
            _logger.LogInformation("回放回测：策略[{S}] 区间内无选股信号", strategy.Name);
            return new BacktestReport { HoldDays = config.HoldDays, Entry = config.Entry.ToString() };
        }

        var bars = await LoadBarsAsync(signals.Select(s => s.Code).Distinct().ToList(), from.Date, ct);
        var report = BacktestEngine.Run(signals, bars, config);
        _logger.LogInformation("回放回测完成：策略[{S}]，{Days} 个交易日，信号 {Sig}，成交 {Exe}，胜率 {Win}%，盈亏比 {Pf}",
            strategy.Name, tradingDays.Count, report.TotalSignals, report.ExecutedTrades, report.WinRatePct, report.ProfitFactor);
        return report;
    }

    private async Task<Dictionary<string, List<BacktestBar>>> LoadBarsAsync(
        List<string> codes, DateTime minDate, CancellationToken ct)
    {
        const string interval = nameof(KlineInterval.Daily);
        var raw = await _db.KlineData
            .Where(k => k.Interval == interval && codes.Contains(k.Code) && k.DateTime >= minDate)
            .Select(k => new { k.Code, k.DateTime, k.Open, k.High, k.Low, k.Close })
            .ToListAsync(ct);
        return raw.GroupBy(k => k.Code)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(k => k.DateTime)
                      .Select(k => new BacktestBar { Date = k.DateTime, Open = k.Open, High = k.High, Low = k.Low, Close = k.Close })
                      .ToList());
    }
}
