using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 趋势跟随策略（右侧）。适用强市 / 主升阶段：追逐已确立上升趋势的强势股，
/// 容忍高 RSI（强者恒强），偏好均线多头发散、价在均线上、量价配合，位置甜区为趋势中段。
/// </summary>
public class TrendStrategy : WeightedSelectionStrategyBase
{
    public override string Key => StrategyKeys.Trend;
    public override string Name => "趋势跟随";
    public override string Description => "追逐均线多头发散、量价齐升的强势股，容忍高 RSI（右侧追强）";
    public override string PreferredRegime => "强市 / 主升";

    /// <summary>趋势策略的极端追高硬顶（独立于 LowDip 的 MaxRise20d，给趋势更大空间）。</summary>
    private const decimal ExtremeRise20d = 100m;

    protected override bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level)
    {
        if (s.MainNetInflow < criteria.MinMainNetInflow) return false;     // 资金门槛（趋势默认较宽）
        if (s.Rise20d > ExtremeRise20d) return false;                      // 仅排除极端透支，不限常规上涨
        // 趋势核心：必须站上中期均线（有均线数据时）
        if (s.Ma20 > 0 && s.Close < s.Ma20) return false;
        // 注意：不按 RSI 超买过滤（趋势容忍强势）
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
            Technical = SelectionScorers.TechnicalTrend(s),   // 趋势技术口径（不罚高 RSI）
            Position = SelectionScorers.PositionTrend(s),     // 趋势位置甜区（中段）
            Form = SelectionScorers.Form(seq),
            DragonTiger = SelectionScorers.DragonTiger(dt),
            Activity = Math.Min(hit.ActivityScore, 100m),
            Theme = SelectionScorers.Theme(s.Code, ctx, out hitHotConcepts),
            Sector = SelectionScorers.Sector(s.Code, ctx),
            Volatility = SelectionScorers.Volatility(seq),
            RelativeStrength = SelectionScorers.RelativeStrength(s, ctx),
        };

    // 趋势策略追强，不惩罚涨停/连板（强势特征）；仅对极端单日暴涨轻微提示由位置分体现。
    protected override decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria) => 0m;

    protected override List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors)
    {
        var tags = new List<string>();
        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0 && s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5)
            tags.Add("均线多头发散");
        if (seq.BreakoutNewHigh) tags.Add("突破新高");
        if (s.Rsi >= 70m) tags.Add("强势RSI");
        if (seq.ConsecutiveLimitUp >= 2) tags.Add($"{seq.ConsecutiveLimitUp}连板");
        else if (s.IsLimitUp) tags.Add("涨停");
        if (s.MainNetInflow > 0) tags.Add($"主力+{SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (factors.Sector >= 80m) tags.Add("强势板块");
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}");
        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) tags.Add("站上均价");
        if (dt != null) tags.Add(dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        return tags;
    }

    protected override string BuildCoreLogic(
        StockSelectionResult r, DailyMarketSnapshotEntity s, SequenceFeatures seq, List<string> hitHotConcepts)
    {
        var parts = new List<string>();
        if (s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Ma20 > 0) parts.Add("均线多头发散，趋势向上");
        if (seq.BreakoutNewHigh) parts.Add("突破前高、打开空间");
        if (s.Rsi >= 70m) parts.Add($"RSI {s.Rsi:F0} 强势");
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (hitHotConcepts.Count > 0) parts.Add($"属热门题材「{hitHotConcepts[0]}」");
        if (parts.Count == 0) parts.Add("站上中期均线，趋势延续");
        return "趋势跟随：" + string.Join("，", parts) + "。右侧追强，注意趋势破位止损。";
    }
}
