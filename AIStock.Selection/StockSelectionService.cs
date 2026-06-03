using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
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
    private readonly IReadOnlyDictionary<string, ISelectionStrategy> _strategies;
    private readonly IDataProviderResolver _resolver;
    private readonly SelectionConfigService _config;
    private readonly ILogger<StockSelectionService> _logger;

    public StockSelectionService(
        AIStockDbContext db,
        IEnumerable<ISelectionStrategy> strategies,
        IDataProviderResolver resolver,
        SelectionConfigService config,
        ILogger<StockSelectionService> logger)
    {
        _db = db;
        _strategies = strategies.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        _resolver = resolver;
        _config = config;
        _logger = logger;
    }

    /// <summary>解析策略键 → 策略实例，未知键回退低吸（默认）。</summary>
    private ISelectionStrategy ResolveStrategy(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key) && _strategies.TryGetValue(key.Trim(), out var s)) return s;
        return _strategies[StrategyKeys.LowDip];
    }

    /// <summary>可用策略清单（key/name/description/适用环境）。</summary>
    public IReadOnlyList<ISelectionStrategy> ListStrategies()
        => _strategies.Values.OrderBy(s => s.Key == StrategyKeys.LowDip ? 0 : 1).ThenBy(s => s.Key).ToList();

    /// <summary>
    /// 执行选股，返回 TOP-N。strategyKey 选择策略（默认低吸 lowdip）；
    /// criteria 为 null 时用配置中心该策略当前生效配置。无快照数据时返回空。
    /// </summary>
    public async Task<List<StockSelectionResult>> SelectAsync(
        SelectionCriteria? criteria = null, string? strategyKey = null, CancellationToken ct = default)
    {
        var strategy = ResolveStrategy(strategyKey);
        criteria ??= await _config.GetActiveCriteriaAsync(strategy.Key, ct);

        var (latest, dragonByCode, sequenceByCode) = await LoadAsync(ct);
        if (latest.Count == 0)
        {
            _logger.LogWarning("daily_market_snapshot 无数据，选股返回空。请先运行 market-snapshot 采集任务。");
            return new List<StockSelectionResult>();
        }

        var regime = await ComputeMarketRegimeAsync(latest, ct);

        // 题材（个股概念 + 当日热门题材）与板块强度，一次性算好供打分与展示复用
        var conceptsByCode = await LoadConceptsByCodeAsync(latest, ct);
        var hotConcepts = ComputeHotConcepts(latest, conceptsByCode);
        var (industryByCode, industryStrength) = await ComputeSectorStrengthAsync(latest, ct);

        var context = new SelectionContext
        {
            Regime = regime,
            HotConcepts = hotConcepts,
            ConceptsByCode = conceptsByCode,
            IndustryByCode = industryByCode,
            IndustryStrength = industryStrength,
        };

        var activePool = ActivityScreener.Screen(latest, criteria);
        var results = strategy.Select(activePool, dragonByCode, sequenceByCode, criteria, context);
        EnrichIndustryConcepts(results, context);
        _logger.LogInformation("选股完成：策略[{Strategy}]，大盘[{Regime}]，活跃池 {Pool} 只，入选 TOP {Top}，当日热门题材 {Hot} 个",
            strategy.Name, regime.Level, activePool.Count, results.Count, hotConcepts.Count);
        return results;
    }

    // 同时判断的主要指数（名称 + 东财 secid）。指数代码前缀与个股不同，需显式 secid。
    private static readonly (string Name, string Secid)[] MarketIndices =
    {
        ("上证", "1.000001"),   // 上证综指
        ("深成", "0.399001"),   // 深证成指
        ("创业", "0.399006"),   // 创业板指
        ("沪深300", "1.000300"),
        ("科创50", "1.000688"), // 上证科创板50成份指数
        ("北证50", "0.899050"), // 北证50
    };

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
            var tasks = MarketIndices.Select(async ix =>
            {
                try
                {
                    var kl = await em.GetIndexDailyAsync(ix.Secid, 30, ct);
                    if (kl.Count < 2) return null;
                    var closes = kl.OrderBy(k => k.DateTime).Select(k => k.Close).ToList();
                    var last = closes[^1];
                    var prev = closes[^2];
                    var ma20 = closes.Count >= 20 ? closes.TakeLast(20).Average() : closes.Average();
                    return new IndexQuote
                    {
                        Name = ix.Name,
                        ChangePercent = prev > 0 ? Math.Round((last - prev) / prev * 100m, 2) : 0,
                        AboveMa20 = last >= ma20,
                    };
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

        // —— 综合打分：指数均值涨跌 / 多数指数是否站上20线 / 全市场涨家占比 ——
        var score = 0;
        if (regime.HasIndex)
        {
            var avgChange = regime.Indices.Average(i => i.ChangePercent);
            if (avgChange > 0.5m) score++;
            else if (avgChange < -0.5m) score--;

            var aboveCount = regime.Indices.Count(i => i.AboveMa20);
            var half = regime.Indices.Count / 2.0;
            if (aboveCount > half) score++;
            else if (aboveCount < half) score--;
        }
        if (regime.AdvanceRatio > 0.55m) score++;
        else if (regime.AdvanceRatio > 0m && regime.AdvanceRatio < 0.4m) score--;

        regime.Level = score >= 2 ? MarketRegimeLevel.Strong
            : score <= -2 ? MarketRegimeLevel.Weak
            : MarketRegimeLevel.Neutral;

        var levelText = regime.Level switch
        {
            MarketRegimeLevel.Strong => "偏强",
            MarketRegimeLevel.Weak => "偏弱（选股趋严）",
            _ => "中性"
        };
        var idxText = regime.HasIndex
            ? string.Join("、", regime.Indices.Select(i => $"{i.Name}{i.ChangePercent:+0.0;-0.0}%{(i.AboveMa20 ? "↑20线" : "↓20线")}"))
            : "指数数据不可用";
        regime.Description = $"大盘{levelText}：{idxText}；涨家占比 {regime.AdvanceRatio:P0}";
        return regime;
    }

    /// <summary>
    /// 计算当日热门题材：当日活跃股（涨停/大涨/放量）扎堆的概念，活跃股越多越热。
    /// 返回 概念→活跃股数。反映"当下市场在炒什么"。
    /// </summary>
    private static Dictionary<string, int> ComputeHotConcepts(
        List<DailyMarketSnapshotEntity> latest, IReadOnlyDictionary<string, List<string>> conceptsByCode)
    {
        var activeCodes = latest
            .Where(s => s.IsLimitUp || s.ChangePercent >= 5m
                || (s.VolumeRatio >= 2m && s.ChangePercent > 0))
            .Select(s => s.Code)
            .ToHashSet();
        if (activeCodes.Count == 0) return new Dictionary<string, int>();

        // 概念 → 活跃股数（去重）
        var counter = new Dictionary<string, HashSet<string>>();
        foreach (var (code, concepts) in conceptsByCode)
        {
            if (!activeCodes.Contains(code)) continue;
            foreach (var c in concepts)
            {
                if (!counter.TryGetValue(c, out var set)) counter[c] = set = new HashSet<string>();
                set.Add(code);
            }
        }
        return counter
            .Where(kv => kv.Value.Count >= 2) // 至少 2 只活跃股才算成"题材"
            .ToDictionary(kv => kv.Key, kv => kv.Value.Count);
    }

    /// <summary>取今日全部股票的概念关联：股票代码 → 概念列表。</summary>
    private async Task<Dictionary<string, List<string>>> LoadConceptsByCodeAsync(
        List<DailyMarketSnapshotEntity> latest, CancellationToken ct)
    {
        var codes = latest.Select(s => s.Code).ToList();
        if (codes.Count == 0) return new();
        return (await _db.StockConceptRelation
                .Where(c => codes.Contains(c.StockCode))
                .Select(c => new { c.StockCode, c.ConceptName })
                .ToListAsync(ct))
            .GroupBy(c => c.StockCode)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConceptName).Distinct().ToList());
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
        var strategy = ResolveStrategy(strategyKey);
        criteria ??= await _config.GetActiveCriteriaAsync(strategy.Key, ct);
        var results = await SelectAsync(criteria, strategy.Key, ct);

        var tradingDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        if (tradingDate == null) return results;

        _db.SelectionResult.Add(new SelectionResultEntity
        {
            TradingDate = tradingDate.Value,
            RunAt = DateTime.Now,
            TopN = criteria.TopN,
            ResultsJson = JsonSerializer.Serialize(results),
        });
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
            ResultsJson = JsonSerializer.Serialize(picks),
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("导入选股结果：{Date}，{Count} 只", tradingDate.ToString("yyyy-MM-dd"), picks.Count);
        return picks.Count;
    }

    /// <summary>选股历史记录元信息（不含明细），按选股时间倒序。</summary>
    public async Task<List<SelectionHistoryItem>> GetHistoryAsync(int take = 30, CancellationToken ct = default)
    {
        var rows = await _db.SelectionResult
            .OrderByDescending(r => r.RunAt)
            .Take(take <= 0 ? 30 : take)
            .Select(r => new { r.Id, r.TradingDate, r.RunAt, r.TopN })
            .ToListAsync(ct);
        return rows.Select(r => new SelectionHistoryItem
        {
            Id = r.Id,
            TradingDate = r.TradingDate,
            RunAt = r.RunAt,
            TopN = r.TopN,
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
