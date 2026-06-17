using System.Collections.Concurrent;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 参数回放回测：用给定参数(SelectionCriteria)+策略，在历史快照上逐交易日重跑选股，
/// 汇总信号后用统一回测引擎评估。
/// 
/// v1.1 优化：
/// - [NEW] 快照内存缓存（同请求内复用，避免重复重建）
/// - [NEW] #8 事件数据覆盖不足告警
/// - [NEW] #9 市值/PE 为 0 静默失败告警
/// - [NEW] 使用统一回测引擎 + 基准对比
/// </summary>
public class ReplayBacktestService
{
    private readonly AIStockDbContext _db;
    private readonly ISelectionStrategyProvider _strategyProvider;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly ILogger<ReplayBacktestService> _logger;

    public ReplayBacktestService(
        AIStockDbContext db, ISelectionStrategyProvider strategyProvider,
        IFeatureCalculator featureCalculator, ILogger<ReplayBacktestService> logger)
    {
        _db = db;
        _strategyProvider = strategyProvider;
        _featureCalculator = featureCalculator;
        _logger = logger;
    }

    /// <summary>
    /// 回放回测：[from,to] 内每个交易日用 criteria 跑策略选股 → 信号 → 回测。
    /// </summary>
    public async Task<BacktestReport> BacktestParamsAsync(
        string? strategyKey, SelectionCriteria criteria, DateTime from, DateTime to,
        BacktestConfig config, CancellationToken ct = default)
    {
        var strategy = await _strategyProvider.ResolveAsync(strategyKey, ct);
        const int seqWindow = 45;

        var since = from.Date.AddDays(-(seqWindow + 15));

        var allShots = await RebuildAllSnapshotsAsync(since, to.Date, ct);
        if (allShots.Count == 0)
        {
            _logger.LogWarning("回放回测：区间内无可重建的快照（检查 kline_data 是否覆盖该区间）");
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

        // 预载区间事件
        var allCodesSet = allCodes.ToHashSet();
        var codeByName = new Dictionary<string, string>();
        foreach (var s in allShots)
            if (!string.IsNullOrEmpty(s.Name)) codeByName[s.Name] = s.Code;
        var newsSince = from.Date.AddDays(-(Math.Max(1, criteria.NewsLookbackDays) + 4));
        var eventsByCode = new Dictionary<string, List<(DateTime Date, string? Type, string? Sentiment, int? Imp, int? Cred, string? Title)>>();

        // ★ #8: 检测事件数据最早可用日期
        DateTime? newsCoverageStart = null;
        try
        {
            var earliestEvent = await _db.EventRecord
                .Where(e => e.RelatedStocks != null && e.RelatedStocks != "")
                .OrderBy(e => e.EventTime ?? e.CreatedAt)
                .Select(e => (DateTime?)(e.EventTime ?? e.CreatedAt))
                .FirstOrDefaultAsync(ct);
            newsCoverageStart = earliestEvent;
        }
        catch { /* DB 查询失败时忽略 */ }

        var evRows = await _db.EventRecord
            .Where(e => e.RelatedStocks != null && e.RelatedStocks != ""
                && (e.EventTime ?? e.CreatedAt) >= newsSince && (e.EventTime ?? e.CreatedAt) <= to.Date.AddDays(1))
            .Select(e => new { e.EventType, e.Sentiment, e.Importance, e.Credibility, e.Title, e.RelatedStocks, D = (e.EventTime ?? e.CreatedAt) })
            .ToListAsync(ct);
        foreach (var r in evRows)
            foreach (var raw in r.RelatedStocks!.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tok = raw.Trim();
                var code = allCodesSet.Contains(tok) ? tok
                    : codeByName.TryGetValue(tok, out var c) ? c : null;
                if (code == null) continue;
                if (!eventsByCode.TryGetValue(code, out var list)) eventsByCode[code] = list = new();
                list.Add((r.D.Date, r.EventType, r.Sentiment, r.Importance, r.Credibility, r.Title));
            }

        var dragonsByDate = (await _db.DragonTiger
                .Where(d => d.Date >= from.Date && d.Date <= to.Date)
                .ToListAsync(ct))
            .GroupBy(d => d.Date.Date)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, DragonTigerEntity>)g.GroupBy(x => x.Code)
                        .ToDictionary(x => x.Key, x => x.First()));

        const string daily = nameof(KlineInterval.Daily);
        var indexSecids = RegimeEvaluator.MarketIndices.Select(i => i.Secid).ToList();
        var indexBarsByCode = (await _db.KlineData
                .Where(k => k.Interval == daily && indexSecids.Contains(k.Code) && k.DateTime <= to.Date)
                .Select(k => new { k.Code, k.DateTime, k.Close })
                .ToListAsync(ct))
            .GroupBy(k => k.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DateTime).Select(x => (x.DateTime, x.Close)).ToList());

        var emptyDragons = new Dictionary<string, DragonTigerEntity>();
        var signals = new List<BacktestSignal>();

        var limitUpByDate = shotsByDate.Keys.OrderBy(d => d)
            .Select(d => (Date: d, Codes: (IReadOnlyCollection<string>)shotsByDate[d]
                .Where(s => s.IsLimitUp || s.ChangePercent >= 9.8m)
                .Select(s => s.Code).ToHashSet()))
            .ToList();

        var patternBarsCache = strategy.UsesPatterns
            ? new Dictionary<string, List<(DateTime Date, CandleBar Bar)>>() : null;
        var patternBarsSince = from.Date.AddDays(-120);

        foreach (var day in tradingDays)
        {
            var dayShots = shotsByDate[day];

            var indices = new List<IndexQuote>();
            foreach (var (name, secid) in RegimeEvaluator.MarketIndices)
            {
                if (!indexBarsByCode.TryGetValue(secid, out var ser)) continue;
                var closes = ser.Where(b => b.DateTime.Date <= day).TakeLast(30).Select(b => b.Close).ToList();
                var q = RegimeEvaluator.QuoteFromCloses(name, closes);
                if (q != null) indices.Add(q);
            }

            var dayHot = SelectionContextBuilder.ComputeHotConcepts(dayShots, conceptsByCode);
            var trailing = limitUpByDate
                .Where(x => x.Date <= day)
                .TakeLast(ConceptLifecycle.WindowDays)
                .Select(x => x.Codes)
                .ToList();
            var stages = ConceptLifecycle.ComputeStages(trailing, conceptsByCode);
            var dayInflow = SelectionContextBuilder.ComputeConceptNetInflow(dayShots, conceptsByCode);
            var dayLeader = SelectionContextBuilder.ComputeConceptLeaderPct(dayShots, conceptsByCode);
            ConceptLifecycle.RemoveFading(dayHot, stages, dayInflow, dayLeader);

            var context = new SelectionContext
            {
                Regime = SelectionContextBuilder.BuildRegime(dayShots, indices),
                HotConcepts = dayHot,
                ConceptsByCode = conceptsByCode,
                IndustryByCode = industryByCode,
                IndustryStrength = SelectionContextBuilder.ComputeSectorStrength(dayShots, industryByCode),
                BenchmarkRise20d = indices.Count > 0
                    ? Math.Round(indices.Average(i => i.Rise20d), 2) : null,
            };

            var activePool = strategy.ScanFullUniverse
                ? ActivityScreener.ScreenAll(dayShots, criteria)
                : ActivityScreener.Screen(dayShots.ToList(), criteria);

            var lookStart = day.AddDays(-(Math.Max(1, criteria.NewsLookbackDays) + 4));
            var dayNews = new Dictionary<string, NewsSignal>();
            var dayKnowledge = new Dictionary<string, List<KnowledgeNote>>();
            foreach (var hit in activePool)
            {
                var code = hit.Snapshot.Code;
                if (!eventsByCode.TryGetValue(code, out var evs)) continue;
                var newsEvs = new List<NewsEvent>();
                foreach (var e in evs)
                {
                    if (e.Date < lookStart || e.Date > day) continue;
                    if (string.Equals(e.Type, "knowledge-star", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!dayKnowledge.TryGetValue(code, out var kl)) dayKnowledge[code] = kl = new();
                        if (kl.Count < 3 && !string.IsNullOrWhiteSpace(e.Title))
                            kl.Add(new KnowledgeNote(e.Title!, e.Sentiment, e.Imp, e.Cred));
                    }
                    else newsEvs.Add(new NewsEvent(e.Type, e.Sentiment, e.Imp, e.Title));
                }
                if (newsEvs.Count > 0)
                {
                    var sig = NewsEventClassifier.Classify(newsEvs);
                    if (sig.Veto || sig.Score != 50m) dayNews[code] = sig;
                }
            }
            context.NewsByCode = dayNews;
            context.KnowledgeNotesByCode = dayKnowledge;
            if (criteria.EnableNewsVeto && dayNews.Count > 0)
                activePool = activePool.Where(h => !(dayNews.TryGetValue(h.Snapshot.Code, out var sg) && sg.Veto)).ToList();

            if (patternBarsCache != null)
            {
                var missing = activePool.Select(h => h.Snapshot.Code)
                    .Where(c => !patternBarsCache.ContainsKey(c)).Distinct().ToList();
                if (missing.Count > 0)
                {
                    var rows = await _db.KlineData
                        .Where(k => k.Interval == daily && missing.Contains(k.Code)
                            && k.DateTime >= patternBarsSince && k.DateTime <= to.Date)
                        .Select(k => new { k.Code, k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume })
                        .ToListAsync(ct);
                    foreach (var c in missing) patternBarsCache[c] = new();
                    foreach (var g in rows.GroupBy(r => r.Code))
                        patternBarsCache[g.Key] = g.OrderBy(r => r.DateTime)
                            .Select(r => (r.DateTime, new CandleBar(r.Open, r.High, r.Low, r.Close, r.Volume)))
                            .ToList();
                }

                var dayPatterns = new Dictionary<string, CandlePatternFeatures>();
                foreach (var hit in activePool)
                {
                    if (!patternBarsCache.TryGetValue(hit.Snapshot.Code, out var pbars) || pbars.Count == 0) continue;
                    var candles = pbars.Where(b => b.Date.Date <= day).Select(b => b.Bar).ToList();
                    if (candles.Count == 0) continue;
                    var pf = CandlePatternAnalyzer.Analyze(candles);
                    if (pf.Any) dayPatterns[hit.Snapshot.Code] = pf;
                }
                context.PatternsByCode = dayPatterns;
            }

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
                signals.Add(new BacktestSignal { Date = day, Code = p.Code, Name = p.Name, Score = p.TotalScore });
        }

        if (signals.Count == 0)
        {
            _logger.LogInformation("回放回测：策略[{S}] 区间内无选股信号", strategy.Name);
            return new BacktestReport { HoldDays = config.HoldDays, Entry = config.Entry.ToString() };
        }

        var bars = await LoadBarsAsync(signals.Select(s => s.Code).Distinct().ToList(), from.Date, ct);

        // 加载基准（沪深300）
        var benchmarkBars = await LoadBenchmarkBarsAsync(from.Date, to.Date, ct);

        var engine = new BacktestEngine();
        var report = engine.Run(signals, bars, config, benchmarkBars);

        // ★ #8: 事件数据覆盖告警
        report.NewsCoverageStart = newsCoverageStart;
        if (newsCoverageStart != null && from.Date < newsCoverageStart.Value.Date)
        {
            report.Warnings.Add(
                $"消息面数据仅覆盖 {newsCoverageStart:yyyy-MM-dd} 之后，" +
                $"此前回测不含消息因子（事件过滤/打分可能偏低）");
        }

        // ★ #9: 市值/PE 为 0 告警
        report.MarketCapAvailable = false;
        if (criteria.MinMarketCap > 0 || criteria.MaxMarketCap > 0)
        {
            report.Warnings.Add(
                "回放快照无历史市值/PE数据，市值筛选条件已自动跳过（不影响非市值因子）");
        }

        // ★ 持久化回测结果
        try
        {
            var configJson = System.Text.Json.JsonSerializer.Serialize(config);
            var reportJson = report.ToSummaryJson(); // 精简：不落 Trades/EquityCurve
            _db.BacktestResult.Add(new AIStock.Infrastructure.Database.Entities.BacktestResultEntity
            {
                RunAt = DateTime.UtcNow,
                BacktestType = "replay",
                StrategyKey = strategy.Key,
                StrategyName = strategy.Name,
                ConfigJson = configJson,
                ReportJson = reportJson,
                ExecutedTrades = report.ExecutedTrades,
                WinRatePct = (decimal)report.WinRatePct,
                AvgReturnPct = (decimal)report.AvgReturnPct,
                SharpeRatio = report.SharpeRatio,
                MaxDrawdownPct = (decimal)report.MaxDrawdownPct,
                AlphaPct = report.Alpha,
                Beta = report.Beta,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("回测结果持久化失败（非致命）：{Msg}", ex.Message);
        }

        _logger.LogInformation("回放回测完成：策略[{S}]，{Days} 个交易日，信号 {Sig}，成交 {Exe}，胜率 {Win}%，盈亏比 {Pf}",
            strategy.Name, tradingDays.Count, report.TotalSignals, report.ExecutedTrades, report.WinRatePct, report.ProfitFactor);
        return report;
    }

    /// <summary>
    /// 用 kline_data + daily_capital_flow 重建 [since,to] 全市场每个交易日的快照（内存）。
    /// 技术面同口径(IFeatureCalculator)，资金面来自资金流表。按股票并行重建
    /// （FeatureCalculatorService / SnapshotRebuilder 均为无状态纯计算，线程安全）。
    /// </summary>
    private async Task<List<DailyMarketSnapshotEntity>> RebuildAllSnapshotsAsync(
        DateTime since, DateTime to, CancellationToken ct)
    {
        const string daily = nameof(KlineInterval.Daily);
        var klineBufStart = since.AddDays(-90); // 宽松缓冲（适应刚上市新股）
        var indexSecids = RegimeEvaluator.MarketIndices.Select(i => i.Secid).ToHashSet();

        var raw = await _db.KlineData
            .Where(k => k.Interval == daily && k.DateTime >= klineBufStart && k.DateTime <= to)
            .Select(k => new { k.Code, k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume, k.Amount, k.TurnoverRate })
            .ToListAsync(ct);

        var flows = (await _db.CapitalFlow
                .Where(c => c.Date >= since && c.Date <= to)
                .Select(c => new { c.Code, c.Date, c.MainNetInflow })
                .ToListAsync(ct))
            .ToDictionary(x => (x.Code, x.Date.Date), x => x.MainNetInflow);

        var names = await _db.StockBase
            .Select(s => new { s.Code, s.Name })
            .ToDictionaryAsync(x => x.Code, x => x.Name, ct);

        var result = new ConcurrentBag<DailyMarketSnapshotEntity>();

        var stockGroups = raw.Where(k => !indexSecids.Contains(k.Code)).GroupBy(k => k.Code).ToList();

        // ★ Phase 5.3: 并行重建快照（按股票并行，每只股票内部日期顺序处理）
        Parallel.ForEach(stockGroups, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, g =>
        {
            var series = g.OrderBy(k => k.DateTime)
                .Select(k => new KlineData
                {
                    DateTime = k.DateTime, Open = k.Open, High = k.High, Low = k.Low,
                    Close = k.Close, Volume = k.Volume, Amount = k.Amount, TurnoverRate = k.TurnoverRate ?? 0,
                })
                .ToList();
            var name = names.TryGetValue(g.Key, out var n) ? n : g.Key;

            for (var i = 0; i < series.Count; i++)
            {
                var d = series[i].DateTime.Date;
                if (d < since || d > to) continue;
                if (i + 1 < 21) continue; // 不足 21 根无法重建

                var window = series.Take(i + 1).ToList();
                var flow = flows.TryGetValue((g.Key, d), out var mf) ? mf : 0m;
                var snap = SnapshotRebuilder.Rebuild(g.Key, name, window, flow, _featureCalculator);
                if (snap != null) result.Add(snap);
            }
        });

        return result.ToList();
    }

    /// <summary>加载基准指数（沪深300）日K。</summary>
    private async Task<List<BacktestBar>?> LoadBenchmarkBarsAsync(
        DateTime minDate, DateTime maxDate, CancellationToken ct)
    {
        try
        {
            const string interval = nameof(KlineInterval.Daily);
            const string hs300 = "000300";
            var rows = await _db.KlineData
                .Where(k => k.Interval == interval && k.Code == hs300
                    && k.DateTime >= minDate.AddDays(-5) && k.DateTime <= maxDate.AddDays(1))
                .OrderBy(k => k.DateTime)
                .Select(k => new { k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume })
                .ToListAsync(ct);
            if (rows.Count == 0) return null;
            return rows.Select(k => new BacktestBar
            {
                Date = k.DateTime, Open = k.Open, High = k.High, Low = k.Low, Close = k.Close, Volume = k.Volume,
            }).ToList();
        }
        catch { return null; }
    }

    private async Task<Dictionary<string, List<BacktestBar>>> LoadBarsAsync(
        List<string> codes, DateTime minDate, CancellationToken ct)
    {
        const string interval = nameof(KlineInterval.Daily);
        var raw = await _db.KlineData
            .Where(k => k.Interval == interval && codes.Contains(k.Code) && k.DateTime >= minDate)
            .Select(k => new { k.Code, k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume })
            .ToListAsync(ct);
        return raw.GroupBy(k => k.Code)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(k => k.DateTime)
                      .Select(k => new BacktestBar
                      {
                          Date = k.DateTime, Open = k.Open, High = k.High, Low = k.Low,
                          Close = k.Close, Volume = k.Volume,
                      })
                      .ToList());
    }
}
