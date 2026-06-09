using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Review;
using AIStock.Selection.Strategies;
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
    private readonly ISelectionStrategyProvider _strategyProvider;
    private readonly IDataProviderResolver _resolver;
    private readonly SelectionConfigService _config;
    private readonly ISelectionReviewQueue _reviewQueue;
    private readonly ILogger<StockSelectionService> _logger;

    public StockSelectionService(
        AIStockDbContext db,
        ISelectionStrategyProvider strategyProvider,
        IDataProviderResolver resolver,
        SelectionConfigService config,
        ISelectionReviewQueue reviewQueue,
        ILogger<StockSelectionService> logger)
    {
        _db = db;
        _strategyProvider = strategyProvider;
        _resolver = resolver;
        _config = config;
        _reviewQueue = reviewQueue;
        _logger = logger;
    }

    /// <summary>解析策略键 → 策略实例（含数据库自建策略），未知键回退低吸（默认）。</summary>
    private Task<ISelectionStrategy> ResolveStrategyAsync(string? key, CancellationToken ct)
        => _strategyProvider.ResolveAsync(key, ct);

    /// <summary>可用策略清单（key/name/description/适用环境，含数据库自建策略）。</summary>
    public Task<IReadOnlyList<ISelectionStrategy>> ListStrategiesAsync(CancellationToken ct = default)
        => _strategyProvider.GetAllAsync(ct);

    /// <summary>
    /// 执行选股，返回 TOP-N。strategyKey 选择策略（默认低吸 lowdip）；
    /// criteria 为 null 时用配置中心该策略当前生效配置。无快照数据时返回空。
    /// </summary>
    public async Task<List<StockSelectionResult>> SelectAsync(
        SelectionCriteria? criteria = null, string? strategyKey = null, CancellationToken ct = default)
    {
        var strategy = await ResolveStrategyAsync(strategyKey, ct);
        criteria ??= await _config.GetActiveCriteriaAsync(strategy.Key, ct);

        var (latest, dragonByCode, sequenceByCode) = await LoadAsync(ct);
        if (latest.Count == 0)
        {
            _logger.LogWarning("daily_market_snapshot 无数据，选股返回空。请先运行 market-snapshot 采集任务。");
            return new List<StockSelectionResult>();
        }

        var regime = await ComputeMarketRegimeAsync(latest, ct);

        // 题材（个股概念 + 当日热门题材）与板块强度，一次性算好供打分与展示复用
        var (conceptsByCode, conceptDigests) = await LoadConceptsByCodeAsync(latest, ct);
        var hotConcepts = ComputeHotConcepts(latest, conceptsByCode);
        var (industryByCode, industryStrength) = await ComputeSectorStrengthAsync(latest, ct);

        var context = new SelectionContext
        {
            Regime = regime,
            HotConcepts = hotConcepts,
            ConceptsByCode = conceptsByCode,
            ConceptDigestByCode = conceptDigests,
            IndustryByCode = industryByCode,
            IndustryStrength = industryStrength,
            BenchmarkRise20d = regime.Indices.Count > 0
                ? Math.Round(regime.Indices.Average(i => i.Rise20d), 2) : null,
        };

        // 全市场扫描策略（如形态类）跳过活跃度粗筛，对全量快照打分；其余策略仍走活跃池
        var scanAll = strategy.ScanFullUniverse;
        var activePool = scanAll
            ? ActivityScreener.ScreenAll(latest, criteria)
            : ActivityScreener.Screen(latest, criteria);

        // K 线形态：全扫时对全市场算（不按代码过滤，避免巨型 IN）；否则只对活跃池算（控成本）
        var latestDate = latest.Max(s => s.Date);
        var patternCodes = scanAll
            ? null
            : activePool.Select(h => h.Snapshot.Code).Distinct().ToList();
        context.PatternsByCode = await LoadPatternsAsync(patternCodes, latestDate, ct);

        // 消息面：近 N 日 news/report 事件 → 分类(排雷/利空/利好)；knowledge-star → 小作文提示（不计分）
        var (newsByCode, knowledgeNotes) = await LoadNewsAsync(activePool, latestDate, criteria.NewsLookbackDays, ct);
        context.NewsByCode = newsByCode;
        context.KnowledgeNotesByCode = knowledgeNotes;
        // 重雷硬否决：池级剔除，覆盖所有策略（含 lowdip 引擎路径）
        if (criteria.EnableNewsVeto && newsByCode.Count > 0)
        {
            var before = activePool.Count;
            activePool = activePool
                .Where(h => !(newsByCode.TryGetValue(h.Snapshot.Code, out var sig) && sig.Veto))
                .ToList();
            if (activePool.Count < before)
                _logger.LogInformation("消息面排雷：剔除 {N} 只命中重雷事件的标的", before - activePool.Count);
        }

        var results = strategy.Select(activePool, dragonByCode, sequenceByCode, criteria, context);
        EnrichIndustryConcepts(results, context);
        EnrichPatterns(results, context);
        EnrichKnowledgeNotes(results, context);
        _logger.LogInformation("选股完成：策略[{Strategy}]，大盘[{Regime}]，活跃池 {Pool} 只，入选 TOP {Top}，当日热门题材 {Hot} 个",
            strategy.Name, regime.Level, activePool.Count, results.Count, hotConcepts.Count);
        return results;
    }

    /// <summary>
    /// 判断大盘环境：综合多个主要指数（当日涨跌幅 + 是否站上 20 日线）与全市场涨跌广度。
    /// 取不到指数时仅用广度判断。弱市选股趋严、强市略放宽（在引擎里生效）。
    /// </summary>
    private async Task<MarketRegime> ComputeMarketRegimeAsync(List<DailyMarketSnapshotEntity> latest, CancellationToken ct)
    {
        var regime = new MarketRegime();
        if (latest.Count > 0)
            regime.AdvanceRatio = Math.Round((decimal)latest.Count(s => s.ChangePercent > 0) / latest.Count, 2);

        var em = _resolver.GetProviders(DataCapability.Kline)
            .FirstOrDefault(p => p.ProviderName == "东方财富") as EastmoneyProvider;
        if (em != null)
        {
            // 多指数并发拉取
            var tasks = RegimeEvaluator.MarketIndices.Select(async ix =>
            {
                try
                {
                    var kl = await em.GetIndexDailyAsync(ix.Secid, 30, ct: ct);
                    var closes = kl.OrderBy(k => k.DateTime).Select(k => k.Close).ToList();
                    return RegimeEvaluator.QuoteFromCloses(ix.Name, closes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "选股：指数 {Name} 获取失败", ix.Name);
                    return null;
                }
            });
            regime.Indices = (await Task.WhenAll(tasks)).Where(q => q != null).Select(q => q!).ToList();
        }
        if (!regime.HasIndex)
            _logger.LogWarning("选股：指数数据均不可用，按全市场涨跌广度判断大盘");

        // —— 综合评估 Level/Kind/推荐策略（与回放共用 RegimeEvaluator，口径一致）——
        regime.LimitUpCount = latest.Count(s => s.IsLimitUp);
        regime.LimitDownCount = latest.Count(s => s.ChangePercent <= -9.8m);
        var (level, kind, rec, kindLabel) = RegimeEvaluator.Evaluate(
            regime.Indices, regime.AdvanceRatio, regime.LimitUpCount, regime.LimitDownCount, latest.Count);
        regime.Level = level;
        regime.Kind = kind;
        regime.RecommendedStrategy = rec;

        var levelText = regime.Level switch
        {
            MarketRegimeLevel.Strong => "偏强",
            MarketRegimeLevel.Weak => "偏弱（选股趋严）",
            _ => "中性"
        };
        var idxText = regime.HasIndex
            ? string.Join("、", regime.Indices.Select(i => $"{i.Name}{i.ChangePercent:+0.0;-0.0}%{(i.AboveMa20 ? "↑20线" : "↓20线")}"))
            : "指数数据不可用";
        regime.Description = $"大盘{levelText}：{idxText}；涨家占比 {regime.AdvanceRatio:P0}；" +
            $"涨停 {regime.LimitUpCount}/跌停 {regime.LimitDownCount}；市场状态：{kindLabel}";
        return regime;
    }

    /// <summary>
    /// 计算当日热门题材：当日活跃股（涨停/大涨/放量）扎堆的概念，活跃股越多越热。
    /// 返回 概念→活跃股数。反映"当下市场在炒什么"。
    /// </summary>
    // 当日热门题材：集中度加权 + 宽筐黑名单 + TopN。实盘/回放共用同一实现，避免口径漂移。
    private static Dictionary<string, int> ComputeHotConcepts(
        List<DailyMarketSnapshotEntity> latest, IReadOnlyDictionary<string, List<string>> conceptsByCode)
        => Backtest.SelectionContextBuilder.ComputeHotConcepts(latest, conceptsByCode);

    /// <summary>取今日全部股票的概念关联：代码→概念列表，以及 代码→(概念→炒作点digest)。</summary>
    private async Task<(Dictionary<string, List<string>> Concepts, Dictionary<string, Dictionary<string, string>> Digests)>
        LoadConceptsByCodeAsync(List<DailyMarketSnapshotEntity> latest, CancellationToken ct)
    {
        var codes = latest.Select(s => s.Code).ToList();
        if (codes.Count == 0) return (new(), new());
        var rows = await _db.StockConceptRelation
            .Where(c => codes.Contains(c.StockCode))
            .Select(c => new { c.StockCode, c.ConceptName, c.ConceptDigest })
            .ToListAsync(ct);
        var concepts = rows.GroupBy(c => c.StockCode)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConceptName).Distinct().ToList());
        var digests = new Dictionary<string, Dictionary<string, string>>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ConceptDigest)) continue;
            if (!digests.TryGetValue(r.StockCode, out var d)) digests[r.StockCode] = d = new();
            d[r.ConceptName] = r.ConceptDigest!;
        }
        return (concepts, digests);
    }

    /// <summary>
    /// 取活跃池个股近 N 日事件：news/report 经 <see cref="NewsEventClassifier"/> 聚合成消息面信号（含重雷否决）；
    /// knowledge-star 单独收集"小作文"标题（仅展示，不计分/不排雷）。回看窗口用日历日+缓冲近似交易日。
    /// </summary>
    private async Task<(Dictionary<string, NewsSignal> News, Dictionary<string, List<string>> Knowledge)> LoadNewsAsync(
        IReadOnlyList<ActivityScreener.ActivityHit> pool, DateTime latestDate, int lookbackDays, CancellationToken ct)
    {
        var news = new Dictionary<string, NewsSignal>();
        var knowledge = new Dictionary<string, List<string>>();
        if (pool.Count == 0) return (news, knowledge);

        // related_stocks 可能是代码（news/report）或名称（knowledge-star）→ 统一按"代码或名称"解析到代码
        var codeSet = pool.Select(h => h.Snapshot.Code).ToHashSet();
        var codeByName = new Dictionary<string, string>();
        foreach (var h in pool)
            if (!string.IsNullOrEmpty(h.Snapshot.Name)) codeByName[h.Snapshot.Name] = h.Snapshot.Code;

        var since = latestDate.AddDays(-(Math.Max(1, lookbackDays) + 4)); // 缓冲覆盖周末/节假

        var rows = await _db.EventRecord
            .Where(e => e.RelatedStocks != null && e.RelatedStocks != ""
                && (e.EventTime ?? e.CreatedAt) >= since && (e.EventTime ?? e.CreatedAt) <= latestDate.AddDays(1))
            .Select(e => new { e.EventType, e.Sentiment, e.Importance, e.Title, e.RelatedStocks })
            .ToListAsync(ct);

        var newsEvents = new Dictionary<string, List<NewsEvent>>();
        foreach (var r in rows)
        {
            var isKs = string.Equals(r.EventType, "knowledge-star", StringComparison.OrdinalIgnoreCase);
            var tokens = r.RelatedStocks!.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                var tok = raw.Trim();
                string code = codeSet.Contains(tok) ? tok
                    : codeByName.TryGetValue(tok, out var c) ? c : null;
                if (code == null) continue;
                if (isKs)
                {
                    if (!knowledge.TryGetValue(code, out var list)) knowledge[code] = list = new();
                    if (list.Count < 3 && !string.IsNullOrWhiteSpace(r.Title)) list.Add(r.Title!);
                }
                else
                {
                    if (!newsEvents.TryGetValue(code, out var list)) newsEvents[code] = list = new();
                    list.Add(new NewsEvent(r.EventType, r.Sentiment, r.Importance, r.Title));
                }
            }
        }

        foreach (var (code, evs) in newsEvents)
        {
            var sig = NewsEventClassifier.Classify(evs);
            if (sig.Veto || sig.Score != 50m) news[code] = sig; // 中性不存，省内存；打分侧缺省即中性
        }
        return (news, knowledge);
    }

    /// <summary>富化：选中票附知识星球"小作文"标题 + 标签（仅提示，不影响选股）。</summary>
    private static void EnrichKnowledgeNotes(List<StockSelectionResult> results, SelectionContext ctx)
    {
        foreach (var r in results)
        {
            if (ctx.KnowledgeNotesByCode.TryGetValue(r.Code, out var notes) && notes.Count > 0)
            {
                r.KnowledgeStarNotes = notes;
                if (!r.Tags.Contains("知识星球·小作文")) r.Tags.Add("知识星球·小作文");
            }
        }
    }

    /// <summary>
    /// 计算板块（行业）强度：按 stock_base.Industry 把今日快照分组，
    /// 用各行业平均涨幅的分位数（0-100）作为强度分；行业内不足 3 只或无行业的记中性。
    /// </summary>
    private async Task<(Dictionary<string, string> IndustryByCode, Dictionary<string, decimal> Strength)>
        ComputeSectorStrengthAsync(List<DailyMarketSnapshotEntity> latest, CancellationToken ct)
    {
        var codes = latest.Select(s => s.Code).ToList();
        var industryByCode = await _db.StockBase
            .Where(s => codes.Contains(s.Code) && s.Industry != null && s.Industry != "")
            .Select(s => new { s.Code, s.Industry })
            .ToDictionaryAsync(x => x.Code, x => x.Industry!, ct);

        var changeByCode = latest.ToDictionary(s => s.Code, s => s.ChangePercent);

        var industryAvg = industryByCode
            .GroupBy(kv => kv.Value)
            .Select(g => new { Industry = g.Key, Codes = g.Select(x => x.Key).ToList() })
            .Where(x => x.Codes.Count >= 3) // 行业内至少 3 只，避免小样本噪声
            .Select(x => new
            {
                x.Industry,
                Avg = x.Codes.Average(c => changeByCode.TryGetValue(c, out var v) ? v : 0m)
            })
            .OrderBy(x => x.Avg)
            .ToList();

        var strength = new Dictionary<string, decimal>();
        for (int i = 0; i < industryAvg.Count; i++)
            strength[industryAvg[i].Industry] = industryAvg.Count <= 1
                ? 50m
                : Math.Round((decimal)i / (industryAvg.Count - 1) * 100m, 1);

        return (industryByCode, strength);
    }

    /// <summary>
    /// 取最近约 120 自然日（覆盖 ~80 交易日，满足二次金叉等需 ≥25 根 K 线的形态）的日 K，
    /// 逐只识别当日命中的 K 线形态。无 K 线数据的股票不入字典（视为未命中）。
    /// codes 为 null=全市场扫描（不按代码过滤）；否则只取指定代码。
    /// </summary>
    private async Task<Dictionary<string, CandlePatternFeatures>> LoadPatternsAsync(
        List<string>? codes, DateTime latestDate, CancellationToken ct)
    {
        if (codes != null && codes.Count == 0) return new();
        const string interval = nameof(KlineInterval.Daily);
        var since = latestDate.AddDays(-120);

        var query = _db.KlineData
            .Where(k => k.Interval == interval && k.DateTime >= since && k.DateTime <= latestDate);
        if (codes != null)
            query = query.Where(k => codes.Contains(k.Code));

        var bars = (await query
                .Select(k => new { k.Code, k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume })
                .ToListAsync(ct))
            .GroupBy(k => k.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DateTime).ToList());

        var result = new Dictionary<string, CandlePatternFeatures>(bars.Count);
        foreach (var (code, list) in bars)
        {
            var candles = list
                .Select(b => new CandleBar(b.Open, b.High, b.Low, b.Close, b.Volume))
                .ToList();
            var pf = CandlePatternAnalyzer.Analyze(candles);
            if (pf.Any) result[code] = pf;
        }
        return result;
    }

    /// <summary>把命中的 K 线形态作为标签补到结果上（展示用），复用已算好的上下文。</summary>
    private static void EnrichPatterns(List<StockSelectionResult> results, SelectionContext ctx)
    {
        foreach (var r in results)
        {
            if (!ctx.PatternsByCode.TryGetValue(r.Code, out var pf) || !pf.Any) continue;
            foreach (var key in pf.Hits)
            {
                var tag = $"形态·{CandlePatternAnalyzer.DisplayName(key)}";
                if (!r.Tags.Contains(tag)) r.Tags.Add(tag);
            }
        }
    }

    /// <summary>补充展示用的行业、概念/题材（命中热门题材的概念优先排序并标记），复用已算好的上下文。</summary>
    private static void EnrichIndustryConcepts(List<StockSelectionResult> results, SelectionContext ctx)
    {
        foreach (var r in results)
        {
            if (ctx.IndustryByCode.TryGetValue(r.Code, out var ind) && !string.IsNullOrEmpty(ind))
                r.Industry = ind;
            if (ctx.ConceptsByCode.TryGetValue(r.Code, out var cs))
            {
                r.Concepts = cs
                    .OrderByDescending(c => ctx.HotConcepts.TryGetValue(c, out var h) ? h : 0)
                    .ThenBy(c => c)
                    .ToList();
                r.HotConcepts = cs.Where(ctx.HotConcepts.ContainsKey).ToList();

                // 命中题材的"炒作点"：概念·LLM蒸馏短语（如"半导体·类ABF膜国产替代"），按热度取前 3
                ctx.ConceptDigestByCode.TryGetValue(r.Code, out var digs);
                if (digs is { Count: > 0 })
                    r.ThemeReasons = r.HotConcepts
                        .Where(c => digs.ContainsKey(c) && !string.IsNullOrWhiteSpace(digs[c]))
                        .Take(3)
                        .Select(c => $"{c}·{digs[c]}")
                        .ToList();
            }
        }
    }

    /// <summary>
    /// 取最近一次已记录的选股结果（盘中刷新结果不跳动）；一条都没有时跑一次并记录。
    /// 需要重新选股请调 <see cref="RunAndSaveAsync"/>（追加新记录，不覆盖）。
    /// </summary>
    public async Task<List<StockSelectionResult>> GetOrCreateLatestAsync(int topN = 5, CancellationToken ct = default)
    {
        var latest = await _db.SelectionResult
            .OrderByDescending(r => r.RunAt)
            .FirstOrDefaultAsync(ct);
        if (latest != null)
            return Deserialize(latest.ResultsJson, topN);

        var criteria = await _config.GetActiveCriteriaAsync(StrategyKeys.LowDip, ct);
        criteria.TopN = topN;
        return await RunAndSaveAsync(criteria, StrategyKeys.LowDip, ct);
    }

    /// <summary>重新选股并追加一条历史记录（不覆盖），返回结果。criteria 为 null 时用该策略生效配置。供手动刷新 / 收盘后任务调用。</summary>
    public async Task<List<StockSelectionResult>> RunAndSaveAsync(
        SelectionCriteria? criteria = null, string? strategyKey = null, CancellationToken ct = default)
    {
        var strategy = await ResolveStrategyAsync(strategyKey, ct);
        criteria ??= await _config.GetActiveCriteriaAsync(strategy.Key, ct);
        var results = await SelectAsync(criteria, strategy.Key, ct);

        var tradingDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        if (tradingDate == null) return results;

        var entity = new SelectionResultEntity
        {
            TradingDate = tradingDate.Value,
            RunAt = DateTime.Now,
            TopN = criteria.TopN,
            Strategy = strategy.Key,
            StrategyName = strategy.Name,
            ResultsJson = JsonSerializer.Serialize(results),
            // LLM 复评改为手动触发：新批次默认未复评，等用户在历史页点「LLM 复评」
            ReviewStatus = "skipped",
        };
        _db.SelectionResult.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("选股结果已记录：{Date} TOP{Top}（追加历史）", tradingDate.Value.ToString("yyyy-MM-dd"), results.Count);
        return results;
    }

    /// <summary>
    /// 导入外部选股结果（如历史未入库的选股），追加一条记录。
    /// RunAt 归到选股交易日 17:00，避免把历史数据当成"最新一次"。
    /// </summary>
    public async Task<int> ImportAsync(List<StockSelectionResult> picks, DateTime tradingDate, CancellationToken ct = default)
    {
        if (picks.Count == 0) return 0;
        _db.SelectionResult.Add(new SelectionResultEntity
        {
            TradingDate = tradingDate.Date,
            RunAt = tradingDate.Date.AddHours(17),
            TopN = picks.Count,
            Strategy = "import",
            StrategyName = "导入",
            ResultsJson = JsonSerializer.Serialize(picks),
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("导入选股结果：{Date}，{Count} 只", tradingDate.ToString("yyyy-MM-dd"), picks.Count);
        return picks.Count;
    }

    /// <summary>
    /// 手动（重新）触发某批选股的 LLM 复评：重置状态为 pending 并入队。返回是否成功（批次存在）。
    /// </summary>
    public async Task<bool> RequestReviewAsync(long id, CancellationToken ct = default)
    {
        var row = await _db.SelectionResult.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row == null) return false;
        row.ReviewStatus = "pending";
        row.ReviewedAt = null;
        await _db.SaveChangesAsync(ct);
        _reviewQueue.Enqueue(id);
        return true;
    }

    /// <summary>选股历史记录元信息（不含明细），按选股时间倒序。</summary>
    public async Task<List<SelectionHistoryItem>> GetHistoryAsync(int take = 30, CancellationToken ct = default)
    {
        var rows = await _db.SelectionResult
            .OrderByDescending(r => r.RunAt)
            .Take(take <= 0 ? 30 : take)
            .Select(r => new { r.Id, r.TradingDate, r.RunAt, r.TopN, r.Strategy, r.StrategyName, r.ReviewStatus })
            .ToListAsync(ct);
        return rows.Select(r => new SelectionHistoryItem
        {
            Id = r.Id,
            TradingDate = r.TradingDate,
            RunAt = r.RunAt,
            TopN = r.TopN,
            Strategy = r.Strategy,
            StrategyName = r.StrategyName,
            ReviewStatus = r.ReviewStatus,
        }).ToList();
    }

    /// <summary>按 id 取某次选股的完整结果。</summary>
    public async Task<List<StockSelectionResult>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var row = await _db.SelectionResult.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row == null ? new() : Deserialize(row.ResultsJson, 0);
    }

    /// <summary>
    /// 某批选股的"选后表现"：以选股日收盘为基准，从日K算 次日/至今累计涨跌、选中后最高涨幅与最低跌幅。
    /// </summary>
    public async Task<SelectionPerformance?> GetPerformanceAsync(long id, CancellationToken ct = default)
    {
        var row = await _db.SelectionResult.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row == null) return null;

        var picks = Deserialize(row.ResultsJson, 0);
        var selDate = row.TradingDate.Date;
        var codes = picks.Select(p => p.Code).Distinct().ToList();
        const string interval = nameof(Core.Enums.KlineInterval.Daily);

        // 取选股日(含)起的日K，用于定基准价 + 区间最高/最低/最新
        var bars = (await _db.KlineData
                .Where(k => k.Interval == interval && codes.Contains(k.Code) && k.DateTime >= selDate)
                .Select(k => new { k.Code, k.DateTime, k.Close, k.High, k.Low })
                .ToListAsync(ct))
            .GroupBy(b => b.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DateTime).ToList());

        DateTime? latestDate = bars.Values.SelectMany(v => v).Select(b => (DateTime?)b.DateTime).Max();

        var items = new List<SelectionPerformanceItem>();
        foreach (var p in picks)
        {
            var item = new SelectionPerformanceItem
            {
                Code = p.Code,
                Name = p.Name,
                SelectClose = p.Close,
                SelectChangePercent = p.ChangePercent,
                CoreLogic = p.CoreLogic,
                Tags = p.Tags ?? new List<string>(),
                RatingStars = p.RatingStars,
                TotalScore = p.TotalScore,
                Review = p.Review,
            };

            if (bars.TryGetValue(p.Code, out var list) && list.Count > 0)
            {
                var baseBar = list.FirstOrDefault(b => b.DateTime.Date == selDate);
                var basePrice = baseBar?.Close ?? (p.Close > 0 ? p.Close : list[0].Close);
                var forward = list.Where(b => b.DateTime.Date > selDate).ToList();
                if (basePrice > 0 && forward.Count > 0)
                {
                    decimal Pct(decimal v) => Math.Round((v - basePrice) / basePrice * 100m, 2);
                    item.NextDayChangePercent = Pct(forward[0].Close);
                    item.CurrentChangePercent = Pct(forward[^1].Close);
                    item.MaxRisePercent = Pct(forward.Max(b => b.High));
                    item.MaxDropPercent = Pct(forward.Min(b => b.Low));
                    item.ForwardDays = forward.Count;
                }
            }
            items.Add(item);
        }

        var withData = items.Where(i => i.CurrentChangePercent.HasValue).ToList();
        return new SelectionPerformance
        {
            Id = row.Id,
            SelectionTradingDate = selDate,
            RunAt = row.RunAt,
            Count = items.Count,
            Strategy = row.Strategy,
            StrategyName = row.StrategyName,
            ReviewStatus = row.ReviewStatus,
            LatestDate = latestDate,
            HitCount = withData.Count(i => i.CurrentChangePercent > 0),
            AvgCurrentChange = withData.Count > 0 ? Math.Round(withData.Average(i => i.CurrentChangePercent!.Value), 2) : 0,
            Items = items,
        };
    }

    private static List<StockSelectionResult> Deserialize(string json, int topN)
    {
        var list = JsonSerializer.Deserialize<List<StockSelectionResult>>(json) ?? new();
        return topN > 0 ? list.Take(topN).ToList() : list;
    }

    /// <summary>仅返回第一级活跃度粗筛池（调试/观察用）。criteria 为 null 时用生效配置。</summary>
    public async Task<List<ActivityScreener.ActivityHit>> ScreenActivityAsync(SelectionCriteria? criteria = null, CancellationToken ct = default)
    {
        criteria ??= await _config.GetActiveCriteriaAsync(StrategyKeys.LowDip, ct);
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
