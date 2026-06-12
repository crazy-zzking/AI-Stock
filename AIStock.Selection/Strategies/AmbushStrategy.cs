using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 吸筹埋伏策略（左侧/事件形态驱动，区别于打分追强）。
/// 目标：在"启动过 → 现在缩量回踩、守平台、振幅收敛"的洗盘吸筹窗口提前埋伏，
/// 抢在二次启动（突破）之前入选，弥补打分制"等已经强了才看得见"的滞后缺陷。
///
/// 关键差异：
/// ① <see cref="ScanFullUniverse"/> = true —— 吸筹股是缩量横盘，会被活跃度粗筛剔除，必须扫全市场；
/// ② 用"硬触发"而非"打分取 TOP-N"决定入选：吸筹形态全中才进，打分只用于在吸筹池内排序。
///
/// 吸筹 vs 出货 的判别靠 HoldsStartLow(回踩不破启动前低) + 缩量回踩企稳 + 振幅收敛，
/// 把"长得像但本质相反"的退潮阴跌挡在外面。
/// </summary>
public class AmbushStrategy : WeightedSelectionStrategyBase
{
    public override string Key => StrategyKeys.Ambush;
    public override string Name => "吸筹埋伏";
    public override string Description => "启动后缩量回踩、守平台、振幅收敛的洗盘吸筹窗口提前埋伏（左侧/全市场扫描）";
    public override string PreferredRegime => "震荡市 / 个股洗盘吸筹期";

    /// <summary>吸筹股缩量横盘，必须跳过活跃度粗筛对全市场扫描。</summary>
    public override bool ScanFullUniverse => true;

    /// <summary>左侧埋伏：豁免"弱市少出手"闸门——震荡/弱市正是吸筹潜伏的时机，不应被动量择时压制。</summary>
    protected override bool AppliesRegimeGate => false;

    protected override bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level)
    {
        // —— 吸筹硬触发：以下全部命中才入选（阈值经 SelectionWeights.Ambush* 可配）——
        var w = criteria.Weights ?? new SelectionWeights();

        // ① 启动背景：近 10 日有过涨停（曾被资金关注、有过启动），否则只是普通死水横盘
        if (seq.LimitUpCountIn10 < Math.Max(1, w.AmbushMinLimitUpIn10)) return false;

        // ② 当前不在拉升中：连板/突破新高 = 吸筹已结束进入拉升，不再是埋伏点
        if (seq.ConsecutiveLimitUp > 0) return false;
        if (seq.BreakoutNewHigh) return false;

        // ③ 当日温和：吸筹横盘期当日不应涨停/大涨（大涨=启动了，不是吸筹）
        if (s.IsLimitUp || s.ChangePercent > criteria.HealthyRiseMax) return false;

        // ④ 缩量回踩企稳（价在 MA10 附近 + 近 3 日缩量 + 当日未大跌）
        if (!seq.PullbackStabilize) return false;

        // ⑤ 守前低：回踩不破启动涨停K线群的最低价（突破不回头，区别于跌破平台的出货）
        if (!seq.HoldsStartLow) return false;

        // ⑥ 振幅收敛：多空分歧收敛、蓄势
        if (!seq.AmplitudeConverging) return false;

        // ⑦（可选）资金承接：洗盘期当日主力净流入不为负，过滤"长得像吸筹的出货阴跌"
        if (w.AmbushRequireInflow && s.MainNetInflow < 0) return false;

        // ⑧（可选）题材共振：板块当下有炒作预期，埋伏才有人来抬轿
        if (w.AmbushRequireHotConcept && !SelectionScorers.HitsHotConcept(s.Code, ctx)) return false;

        // 排除传统低弹性大盘股
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
            Technical = SelectionScorers.Technical(s),    // 低吸口径（不奖励超买）
            Position = SelectionScorers.Position(s),      // 低位甜区
            Form = SelectionScorers.Form(seq),            // 含缩量回踩企稳加分
            DragonTiger = SelectionScorers.DragonTiger(dt),
            Activity = Math.Min(hit.ActivityScore, 100m),
            Theme = SelectionScorers.Theme(s.Code, ctx, out hitHotConcepts),
            Sector = SelectionScorers.Sector(s.Code, ctx),
            Volatility = SelectionScorers.Volatility(seq), // 低波动=吸筹稳=高分（埋伏内排序主轴之一）
            RelativeStrength = SelectionScorers.RelativeStrength(s, ctx),
            News = SelectionScorers.News(s.Code, ctx),
        };

    // 吸筹期本就温和横盘，无涨停可罚。
    protected override decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria) => 0m;

    /// <summary>排序用"吸筹质量分"（不含动量），避免动量打分把真吸筹票挤掉、把破位票顶上来。</summary>
    protected override decimal ComputeTotalScore(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionFactorScores factors,
        SelectionWeights w, decimal scoreFactor, SelectionCriteria criteria)
        => SelectionScorers.AmbushQuality(s, seq);

    protected override List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors)
    {
        var tags = new List<string> { "缩量回踩企稳", "守前低", "振幅收敛" };
        if (seq.LimitUpCountIn10 >= 1) tags.Add($"近10日{seq.LimitUpCountIn10}次涨停启动");
        if (seq.AvgAmplitude5 > 0) tags.Add($"振幅{seq.AvgAmplitude5:0.#}%");
        if (s.MainNetInflow > 0) tags.Add($"主力+{SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}");
        if (dt != null) tags.Add(dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        return tags;
    }

    protected override string BuildCoreLogic(
        StockSelectionResult r, DailyMarketSnapshotEntity s, SequenceFeatures seq, List<string> hitHotConcepts)
    {
        var parts = new List<string>
        {
            $"近10日{seq.LimitUpCountIn10}次涨停启动后缩量回踩 MA10 企稳",
            "回踩不破启动前低、振幅收敛（洗盘吸筹特征）",
        };
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (hitHotConcepts.Count > 0) parts.Add($"属热门题材「{hitHotConcepts[0]}」");
        return "吸筹埋伏：" + string.Join("，", parts) + "。左侧潜伏二次启动，跌破平台/放量阴跌则证伪止损。";
    }
}
