using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Narration;

namespace AIStock.Selection;

/// <summary>
/// 第二级漏斗：多因子打分引擎（LowDip/埋伏型口径）。在活跃池上按"主力净流入 + 技术面多头未超买 +
/// 20日涨幅非追高 + 龙虎榜阵容"加权打分，排序取 TOP-N，生成星级/标签/核心逻辑。
/// 单因子打分复用 <see cref="SelectionScorers"/>，本类负责权重组合、硬过滤与展示富化。纯计算，便于单测。
/// </summary>
public class StockSelectionEngine
{
    private readonly ILogicNarrator _narrator;

    public StockSelectionEngine(ILogicNarrator narrator) => _narrator = narrator;

    /// <summary>无多日序列时的便利重载（序列特征退化为空，仅按单日打分）。</summary>
    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        SelectionCriteria criteria)
        => Select(activePool, dragonTigerByCode,
            new Dictionary<string, SequenceFeatures>(), criteria);

    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        IReadOnlyDictionary<string, SequenceFeatures> sequenceByCode,
        SelectionCriteria criteria,
        SelectionContext? context = null)
    {
        var results = new List<StockSelectionResult>();

        // 大盘环境调节：弱市收紧（压追高线、要求资金净流入、整体降权），强市略放宽
        var regime = context?.Regime;
        var level = regime?.Level ?? MarketRegimeLevel.Neutral;
        var maxRise20dEff = level == MarketRegimeLevel.Weak
            ? Math.Min(criteria.MaxRise20d, 30m)
            : criteria.MaxRise20d;
        var minInflowEff = level == MarketRegimeLevel.Weak
            ? Math.Max(criteria.MinMainNetInflow, 1m)   // 弱市要求主力实打实净流入（>0）
            : criteria.MinMainNetInflow;
        var w = criteria.Weights ?? new SelectionWeights();
        var scoreFactor = level switch
        {
            MarketRegimeLevel.Weak => w.RegimeWeakFactor,
            MarketRegimeLevel.Strong => w.RegimeStrongFactor,
            _ => 1.0m
        };
        var regimeNote = regime?.Description ?? string.Empty;

        foreach (var hit in activePool)
        {
            var s = hit.Snapshot;

            // —— 硬过滤（受大盘环境调节）——
            if (s.MainNetInflow < minInflowEff) continue; // 主力净流入门槛（弱市要求 >0）
            if (s.Rise20d > maxRise20dEff) continue;       // 追高排除（弱市压到 30%）
            if (s.Rsi > criteria.MaxRsi) continue;         // 超买排除

            // 排除"传统低弹性行业 且 大市值"的票（如银行/电力/高速大盘股，涨不动）——两条件同时满足才剔除
            if (criteria.ExcludeTraditionalIndustry && criteria.MaxTotalMarketCap > 0 &&
                s.TotalMarketCap > criteria.MaxTotalMarketCap &&
                context != null && context.IndustryByCode.TryGetValue(s.Code, out var industry) &&
                !string.IsNullOrEmpty(industry) &&
                criteria.ExcludeIndustryKeywords.Any(k => industry.Contains(k)))
                continue;

            dragonTigerByCode.TryGetValue(s.Code, out var dt);
            if (criteria.RequireDragonTiger && dt == null) continue;

            var seq = sequenceByCode.TryGetValue(s.Code, out var sf) ? sf : new SequenceFeatures();

            // —— 因子打分（0-100，复用共享打分器）——
            var capital = SelectionScorers.Capital(s, seq);   // 含连续净流入加成
            var technical = SelectionScorers.Technical(s);
            var position = SelectionScorers.Position(s);
            var form = SelectionScorers.Form(seq);            // 多日形态
            var dragon = SelectionScorers.DragonTiger(dt);
            var activity = Math.Min(hit.ActivityScore, 100m);
            var theme = SelectionScorers.Theme(s.Code, context, out var hitHotConcepts);  // 题材合力
            var sector = SelectionScorers.Sector(s.Code, context);                        // 板块强弱
            var volatility = SelectionScorers.Volatility(seq);                            // 波动/风险（低波动高分）
            var relStrength = SelectionScorers.RelativeStrength(s, context);              // 相对大盘强度
            var news = SelectionScorers.News(s.Code, context);                            // 消息面（默认权重0）

            var total = capital * w.Capital + technical * w.Technical + position * w.Position
                        + form * w.Form + dragon * w.DragonTiger + activity * w.Activity
                        + theme * w.Theme + sector * w.Sector + volatility * w.Volatility
                        + relStrength * w.RelativeStrength + news * w.News;

            // 涨停性质惩罚（多日）：区分低位首板（仍有空间，轻罚）与高位/连板（追高风险，重罚）
            total -= SelectionScorers.LimitUpPenalty(s, seq, criteria);
            total *= scoreFactor;                  // 大盘环境整体调节
            total = Math.Clamp(total, 0m, 100m);

            var result = new StockSelectionResult
            {
                Code = s.Code,
                Name = s.Name,
                Close = s.Close,
                ChangePercent = s.ChangePercent,
                TotalMarketCap = s.TotalMarketCap,
                Rise20d = s.Rise20d,
                PeTtm = s.PeTtm,
                Bbi = s.Bbi,
                MainNetInflow = s.MainNetInflow,
                ConsecutiveInflowDays = seq.ConsecutiveInflowDays,
                ConsecutiveLimitUp = seq.ConsecutiveLimitUp,
                TotalScore = Math.Round(total, 1),
                RatingStars = SelectionScorers.ToStars(total),
                Tags = BuildTags(s, dt, hit.Features, seq, hitHotConcepts, sector),
                Factors = new SelectionFactorScores
                {
                    Capital = Math.Round(capital, 1),
                    Technical = Math.Round(technical, 1),
                    Position = Math.Round(position, 1),
                    DragonTiger = Math.Round(dragon, 1),
                    Activity = Math.Round(activity, 1),
                    Form = Math.Round(form, 1),
                    Theme = Math.Round(theme, 1),
                    Sector = Math.Round(sector, 1),
                    Volatility = Math.Round(volatility, 1),
                    RelativeStrength = Math.Round(relStrength, 1),
                    News = Math.Round(news, 1)
                }
            };

            result.CoreLogic = _narrator.Narrate(result, new NarrationContext
            {
                Snapshot = s,
                DragonTiger = dt,
                ActivityFeatures = hit.Features
            });
            result.MarketRegime = regimeNote;
            result.RecommendedStrategy = regime?.RecommendedStrategy ?? string.Empty;

            results.Add(result);
        }

        // 出手闸门：弱市/风险释放收紧分数下限并压缩出手数量（中性/强市零改动）
        return RegimeGate.Apply(
            results.OrderByDescending(r => r.TotalScore),
            criteria, level, regime?.Kind ?? RegimeKind.Range);
    }

    private static List<string> BuildTags(DailyMarketSnapshotEntity s, DragonTigerEntity? dt, List<string> activityFeatures,
        SequenceFeatures seq, List<string> hitHotConcepts, decimal sectorScore)
    {
        var tags = new List<string>();
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}"); // 命中最热题材
        if (sectorScore >= 80m) tags.Add("强势板块");
        if (s.MainNetInflow > 0)
            tags.Add(seq.ConsecutiveInflowDays >= 2
                ? $"主力连{seq.ConsecutiveInflowDays}日+{SelectionScorers.FormatWan(s.MainNetInflow)}"
                : $"主力+{SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0 && s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5)
            tags.Add("多头排列");
        if (s.MacdGoldenCross) tags.Add("MACD刚金叉");
        if (s.Rsi is > 0m and < 40m) tags.Add("超卖反弹");
        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) tags.Add("站上均价");
        if (s.Bbi > 0 && s.Close >= s.Bbi) tags.Add("站上BBI");
        if (seq.BreakoutNewHigh) tags.Add("突破新高");
        else if (seq.PullbackStabilize) tags.Add("回踩企稳");
        if (activityFeatures.Contains("温和放量")) tags.Add("温和放量");
        else if (seq.ConsecutiveLimitUp >= 2) tags.Add($"{seq.ConsecutiveLimitUp}连板");
        else if (s.IsLimitUp) tags.Add("首板");
        if (dt != null)
        {
            var instCount = SelectionScorers.ParseSeats(dt.BuySeatsJson).Count(x => x.IsInstitution);
            tags.Add(instCount > 0 ? $"龙虎榜·机构{instCount}席"
                : dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        }
        return tags;
    }
}
