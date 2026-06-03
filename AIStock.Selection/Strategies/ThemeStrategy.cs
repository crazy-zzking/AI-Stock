using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 题材龙头策略。适用题材行情：只在命中当日热门题材（活跃股扎堆的概念）的个股里选，
/// 侧重题材合力 + 板块强度 + 龙头带动，规避超买与极端追高。
/// </summary>
public class ThemeStrategy : WeightedSelectionStrategyBase
{
    public override string Key => StrategyKeys.Theme;
    public override string Name => "题材龙头";
    public override string Description => "只选命中当日热门题材的个股，侧重题材合力/板块强度/龙头带动";
    public override string PreferredRegime => "题材市 / 情绪活跃";

    protected override bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level)
    {
        // 题材策略的核心硬门槛：必须命中当日热门题材
        if (!SelectionScorers.HitsHotConcept(s.Code, ctx)) return false;
        if (s.MainNetInflow < criteria.MinMainNetInflow) return false;
        if (s.Rsi > criteria.MaxRsi) return false;                         // 题材也防过热
        var maxRise = level == MarketRegimeLevel.Weak ? Math.Min(criteria.MaxRise20d, 30m) : criteria.MaxRise20d;
        if (s.Rise20d > maxRise) return false;                             // 防高位接盘
        if (SelectionScorers.IsExcludedTraditionalBigCap(s, criteria, ctx)) return false;
        if (criteria.RequireDragonTiger && dt == null) return false;
        return true;
    }

    protected override SelectionFactorScores ComputeFactors(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionContext? ctx, out List<string> hitHotConcepts)
        => new()
        {
            Capital = SelectionScorers.Capital(s, seq),
            Technical = SelectionScorers.Technical(s),
            Position = SelectionScorers.Position(s),
            Form = SelectionScorers.Form(seq),
            DragonTiger = SelectionScorers.DragonTiger(dt),
            Activity = Math.Min(hit.ActivityScore, 100m),
            Theme = SelectionScorers.Theme(s.Code, ctx, out hitHotConcepts),
            Sector = SelectionScorers.Sector(s.Code, ctx),
        };

    // 题材龙头常涨停，沿用低吸口径的涨停惩罚以防高位连板接盘。
    protected override decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria)
        => SelectionScorers.LimitUpPenalty(s, seq, criteria);

    protected override List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors)
    {
        var tags = new List<string>();
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}");
        if (hitHotConcepts.Count > 1) tags.Add($"+{hitHotConcepts.Count - 1}题材");
        if (factors.Sector >= 80m) tags.Add("强势板块");
        if (seq.ConsecutiveLimitUp >= 2) tags.Add($"{seq.ConsecutiveLimitUp}连板");
        else if (s.IsLimitUp) tags.Add("首板");
        if (s.MainNetInflow > 0) tags.Add($"主力+{SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (dt != null) tags.Add(dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        return tags;
    }

    protected override string BuildCoreLogic(
        StockSelectionResult r, DailyMarketSnapshotEntity s, SequenceFeatures seq, List<string> hitHotConcepts)
    {
        var parts = new List<string>();
        if (hitHotConcepts.Count > 0)
            parts.Add($"命中热门题材「{hitHotConcepts[0]}」{(hitHotConcepts.Count > 1 ? $"等 {hitHotConcepts.Count} 个风口" : "")}");
        if (r.Factors.Sector >= 80m) parts.Add("所属板块当日强势");
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (seq.ConsecutiveLimitUp >= 1 || s.IsLimitUp) parts.Add("有涨停带动");
        if (parts.Count == 0) parts.Add("题材活跃、资金关注");
        return "题材龙头：" + string.Join("，", parts) + "。题材退潮快，注意及时兑现。";
    }
}
