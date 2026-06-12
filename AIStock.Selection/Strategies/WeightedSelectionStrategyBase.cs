using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 加权多因子策略基类：统一"硬过滤 → 8 因子打分 → 权重加权 → 涨停惩罚 → 大盘系数 → TOP-N"骨架，
/// 子类只需定义各自的过滤口径、因子取值、标签与核心逻辑文字。保证打分流程一致、便于对比。
/// </summary>
public abstract class WeightedSelectionStrategyBase : ISelectionStrategy
{
    public abstract string Key { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string PreferredRegime { get; }
    public virtual bool ScanFullUniverse => false;
    public virtual bool UsesPatterns => false;

    /// <summary>是否应用"弱市出手闸门"（弧市压缩 TopN/分数下限）。右侧动量策略=true；左侧埋伏策略可覆盖为 false。</summary>
    protected virtual bool AppliesRegimeGate => true;

    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        IReadOnlyDictionary<string, SequenceFeatures> sequenceByCode,
        SelectionCriteria criteria,
        SelectionContext? context = null)
    {
        var w = criteria.Weights ?? new SelectionWeights();
        var level = context?.Regime?.Level ?? MarketRegimeLevel.Neutral;
        var scoreFactor = SelectionScorers.RegimeFactor(level, w);
        var regimeNote = context?.Regime?.Description ?? string.Empty;

        var results = new List<StockSelectionResult>();

        foreach (var hit in activePool)
        {
            var s = hit.Snapshot;
            var seq = sequenceByCode.TryGetValue(s.Code, out var sf) ? sf : new SequenceFeatures();
            dragonTigerByCode.TryGetValue(s.Code, out var dt);

            if (!PassesHardFilter(s, seq, dt, hit, criteria, context, level)) continue;

            var factors = ComputeFactors(s, seq, dt, hit, context, out var hitHotConcepts);

            var total = ComputeTotalScore(s, seq, factors, w, scoreFactor, criteria);

            var result = new StockSelectionResult
            {
                Code = s.Code,
                Name = s.Name,
                Close = s.Close,
                ChangePercent = s.ChangePercent,
                TotalMarketCap = s.TotalMarketCap,
                Rise20d = s.Rise20d,
                PeTtm = s.PeTtm,
                MainNetInflow = s.MainNetInflow,
                ConsecutiveInflowDays = seq.ConsecutiveInflowDays,
                ConsecutiveLimitUp = seq.ConsecutiveLimitUp,
                TotalScore = Math.Round(total, 1),
                RatingStars = SelectionScorers.ToStars(total),
                Tags = BuildTags(s, dt, hit, seq, hitHotConcepts, factors),
                Factors = new SelectionFactorScores
                {
                    Capital = Math.Round(factors.Capital, 1),
                    Technical = Math.Round(factors.Technical, 1),
                    Position = Math.Round(factors.Position, 1),
                    DragonTiger = Math.Round(factors.DragonTiger, 1),
                    Activity = Math.Round(factors.Activity, 1),
                    Form = Math.Round(factors.Form, 1),
                    Theme = Math.Round(factors.Theme, 1),
                    Sector = Math.Round(factors.Sector, 1),
                    Volatility = Math.Round(factors.Volatility, 1),
                    RelativeStrength = Math.Round(factors.RelativeStrength, 1),
                    News = Math.Round(factors.News, 1),
                },
                MarketRegime = regimeNote,
                RecommendedStrategy = context?.Regime?.RecommendedStrategy ?? string.Empty,
            };
            result.CoreLogic = BuildCoreLogic(result, s, seq, hitHotConcepts);

            results.Add(result);
        }

        var ordered = results.OrderByDescending(r => r.TotalScore);
        // 出手闸门：弱市/风险释放收紧分数下限并压缩出手数量（中性/强市零改动）。
        // 左侧埋伏策略豁免（弱市/震荡正是吸筹潜伏时机，不受动量择时压制）。
        return AppliesRegimeGate
            ? RegimeGate.Apply(ordered, criteria, level, context?.Regime?.Kind ?? RegimeKind.Range)
            : ordered.Take(criteria.TopN).ToList();
    }

    /// <summary>硬过滤：返回 false 则淘汰。各策略口径不同。</summary>
    protected abstract bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level);

    /// <summary>计算 8 因子原始分（不四舍五入），并输出命中的热门题材。</summary>
    protected abstract SelectionFactorScores ComputeFactors(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionContext? ctx, out List<string> hitHotConcepts);

    /// <summary>涨停/追高惩罚（默认无）。</summary>
    protected virtual decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria) => 0m;

    /// <summary>
    /// 综合分（决定排序与入选）。默认 = 8 因子加权 − 涨停惩罚 × 大盘环境系数。
    /// 左侧埋伏类策略可覆盖为"吸筹质量分"等不含动量的口径，避免动量打分把真吸筹票挤掉。
    /// </summary>
    protected virtual decimal ComputeTotalScore(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionFactorScores factors,
        SelectionWeights w, decimal scoreFactor, SelectionCriteria criteria)
    {
        var total = SelectionScorers.WeightedTotal(factors, w);
        total -= Penalty(s, seq, criteria);
        total *= scoreFactor;
        return Math.Clamp(total, 0m, 100m);
    }

    protected abstract List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors);

    protected abstract string BuildCoreLogic(
        StockSelectionResult r, DailyMarketSnapshotEntity s, SequenceFeatures seq, List<string> hitHotConcepts);

    /// <summary>展示用：补行业 / 概念 / 命中题材。由 Service 富化，这里留空。</summary>
}
