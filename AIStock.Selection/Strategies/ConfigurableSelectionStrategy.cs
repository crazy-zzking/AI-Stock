using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 数据驱动的可配置策略：由一份 <see cref="StrategyDefinition"/> 驱动 4 个钩子，
/// 复用 WeightedSelectionStrategyBase 的统一打分骨架。用户在前端组合过滤口径/因子口径/权重即得新策略，
/// 无需写代码。内置 lowdip/trend/theme 也可用本类表达（见 <see cref="BuiltinStrategyDefinitions"/>）。
/// </summary>
public class ConfigurableSelectionStrategy : WeightedSelectionStrategyBase
{
    private readonly StrategyDefinition _def;

    public ConfigurableSelectionStrategy(StrategyDefinition def) => _def = def;

    public override string Key => _def.Key;
    public override string Name => _def.Name;
    public override string Description => _def.Description;
    public override string PreferredRegime => _def.PreferredRegime;
    public override bool ScanFullUniverse => _def.ScanFullUniverse;
    public override bool UsesPatterns => _def.Filters.RequirePatterns is { Count: > 0 };

    protected override bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level)
    {
        var f = _def.Filters;

        if (f.RequireHotConcept && !SelectionScorers.HitsHotConcept(s.Code, ctx)) return false;

        if (f.ByMinInflow)
        {
            var minInflow = f.WeakRegimeRequireInflow && level == MarketRegimeLevel.Weak
                ? Math.Max(criteria.MinMainNetInflow, 1m)
                : criteria.MinMainNetInflow;
            if (s.MainNetInflow < minInflow) return false;
        }

        if (f.ByRsi && s.Rsi > criteria.MaxRsi) return false;

        if (f.ByRise20d)
        {
            var maxRise = f.WeakRegimeTightenRise20d && level == MarketRegimeLevel.Weak
                ? Math.Min(criteria.MaxRise20d, 30m)
                : criteria.MaxRise20d;
            if (s.Rise20d > maxRise) return false;
        }

        if (f.ExtremeRise20d is decimal ext && s.Rise20d > ext) return false;

        if (f.RequireAboveMa20 && s.Ma20 > 0 && s.Close < s.Ma20) return false;

        if (f.ExcludeTraditionalBigCap && SelectionScorers.IsExcludedTraditionalBigCap(s, criteria, ctx)) return false;

        if (criteria.RequireDragonTiger && dt == null) return false;

        // K 线形态硬过滤：要求命中指定形态（OR / 可选 AND）。
        // 运行时 criteria.Patterns 非空则覆盖策略定义的形态列表，实现"选股时自选形态"。
        if (f.RequirePatterns is { Count: > 0 })
        {
            var required = criteria.Patterns is { Count: > 0 } ? criteria.Patterns : f.RequirePatterns;
            if (ctx == null || !ctx.PatternsByCode.TryGetValue(s.Code, out var pf) || !pf.Any) return false;
            var match = (criteria.RequireAllPatterns || f.RequireAllPatterns)
                ? required.All(pf.Hits.Contains)
                : required.Any(pf.Hits.Contains);
            if (!match) return false;
        }

        return true;
    }

    protected override SelectionFactorScores ComputeFactors(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionContext? ctx, out List<string> hitHotConcepts)
        => new()
        {
            Capital = SelectionScorers.Capital(s, seq),
            Technical = string.Equals(_def.FactorKinds.Technical, "trend", StringComparison.OrdinalIgnoreCase)
                ? SelectionScorers.TechnicalTrend(s) : SelectionScorers.Technical(s),
            Position = string.Equals(_def.FactorKinds.Position, "trend", StringComparison.OrdinalIgnoreCase)
                ? SelectionScorers.PositionTrend(s) : SelectionScorers.Position(s),
            Form = SelectionScorers.Form(seq),
            DragonTiger = SelectionScorers.DragonTiger(dt),
            Activity = Math.Min(hit.ActivityScore, 100m),
            Theme = SelectionScorers.Theme(s.Code, ctx, out hitHotConcepts),
            Sector = SelectionScorers.Sector(s.Code, ctx),
            Volatility = SelectionScorers.Volatility(seq),
            RelativeStrength = SelectionScorers.RelativeStrength(s, ctx),
        };

    protected override decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria)
        => string.Equals(_def.Penalty, "limitup", StringComparison.OrdinalIgnoreCase)
            ? SelectionScorers.LimitUpPenalty(s, seq, criteria) : 0m;

    protected override List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors)
    {
        var tags = new List<string>();
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}");
        if (factors.Sector >= 80m) tags.Add("强势板块");
        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0 && s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5)
            tags.Add("多头排列");
        if (s.MacdGoldenCross) tags.Add("MACD刚金叉");
        if (s.MainNetInflow > 0)
            tags.Add(seq.ConsecutiveInflowDays >= 2
                ? $"主力连{seq.ConsecutiveInflowDays}日+{SelectionScorers.FormatWan(s.MainNetInflow)}"
                : $"主力+{SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (seq.ConsecutiveLimitUp >= 2) tags.Add($"{seq.ConsecutiveLimitUp}连板");
        else if (s.IsLimitUp) tags.Add("涨停");
        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) tags.Add("站上均价");
        if (dt != null) tags.Add(dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        return tags;
    }

    protected override string BuildCoreLogic(
        StockSelectionResult r, DailyMarketSnapshotEntity s, SequenceFeatures seq, List<string> hitHotConcepts)
    {
        if (!string.IsNullOrWhiteSpace(_def.CoreLogicTemplate)) return _def.CoreLogicTemplate!;
        var parts = new List<string>();
        if (hitHotConcepts.Count > 0) parts.Add($"属热门题材「{hitHotConcepts[0]}」");
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Ma20 > 0) parts.Add("均线多头");
        if (parts.Count == 0) parts.Add("多因子综合评分占优");
        return $"{Name}：" + string.Join("，", parts) + "。";
    }
}
