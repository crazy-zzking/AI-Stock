using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 题材动量策略。捕捉"题材风口 + 资金进场 + 量能放大 + 技术转强且未超买"的进攻型品种。
/// 硬过滤：命中当日热门题材 + 主力净流入达标 + RSI 未超买 + 量比放大 + 收盘站上 BBI（多空线多头）。
/// 打分侧重 资金/题材/相对强度/技术(含 MACD 金叉、BBI)。区别于"低吸埋伏"的左侧潜伏，
/// 本策略偏右侧、追逐当日强势资金共识。涨停不惩罚（动量容忍），但仍要求量比与 BBI 多头以防杂毛追高。
/// </summary>
public class HotMoneyStrategy : WeightedSelectionStrategyBase
{
    public override string Key => StrategyKeys.HotMoney;
    public override string Name => "题材动量";
    public override string Description => "题材风口 + 主力净流入 + 量比放大 + RSI未超买 + 站上BBI 的进攻型动量股（右侧）";
    public override string PreferredRegime => "题材市 / 强势 / 情绪活跃";

    protected override bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level)
    {
        // 题材共振：必须命中当日热门题材
        if (!SelectionScorers.HitsHotConcept(s.Code, ctx)) return false;
        // 主力净流入达标（弱市要求实打实 >0）
        var minInflow = level == MarketRegimeLevel.Weak ? Math.Max(criteria.MinMainNetInflow, 1m) : criteria.MinMainNetInflow;
        if (s.MainNetInflow < minInflow) return false;
        // RSI 未超买
        if (s.Rsi > criteria.MaxRsi) return false;
        // 量比放大（量能确认）
        if (s.VolumeRatio < criteria.MinVolumeRatio) return false;
        // 收盘站上 BBI（多空线多头占优）
        if (s.Bbi > 0 && s.Close < s.Bbi) return false;
        // 排除"传统低弹性行业 + 大市值"
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
            Technical = SelectionScorers.Technical(s),          // 含 MACD 金叉 / 站上 BBI 加分
            Position = SelectionScorers.Position(s),
            Form = SelectionScorers.Form(seq),
            DragonTiger = SelectionScorers.DragonTiger(dt),
            Activity = Math.Min(hit.ActivityScore, 100m),
            Theme = SelectionScorers.Theme(s.Code, ctx, out hitHotConcepts),
            Sector = SelectionScorers.Sector(s.Code, ctx),
            Volatility = SelectionScorers.Volatility(seq),
            RelativeStrength = SelectionScorers.RelativeStrength(s, ctx),  // 相对大盘强度
            News = SelectionScorers.News(s.Code, ctx),                    // 消息面（默认权重0）
        };

    // 动量口径：不惩罚涨停（追强），但靠量比/BBI 硬门槛把控质量。
    protected override decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria) => 0m;

    protected override List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors)
    {
        var tags = new List<string>();
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}");
        if (hitHotConcepts.Count > 1) tags.Add($"+{hitHotConcepts.Count - 1}题材");
        if (factors.Sector >= 80m) tags.Add("强势板块");
        if (s.VolumeRatio >= criteriaVrTagThreshold) tags.Add($"量比{s.VolumeRatio:0.#}");
        if (s.MacdGoldenCross) tags.Add("MACD刚金叉");
        if (s.Bbi > 0 && s.Close >= s.Bbi) tags.Add("站上BBI");
        if (factors.RelativeStrength >= 70m) tags.Add("强于大盘");
        if (s.MainNetInflow > 0) tags.Add($"主力+{SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (seq.ConsecutiveLimitUp >= 2) tags.Add($"{seq.ConsecutiveLimitUp}连板");
        else if (s.IsLimitUp) tags.Add("涨停");
        if (dt != null) tags.Add(dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        return tags;
    }

    private const decimal criteriaVrTagThreshold = 1.5m;

    protected override string BuildCoreLogic(
        StockSelectionResult r, DailyMarketSnapshotEntity s, SequenceFeatures seq, List<string> hitHotConcepts)
    {
        var parts = new List<string>();
        if (hitHotConcepts.Count > 0)
            parts.Add($"命中热门题材「{hitHotConcepts[0]}」{(hitHotConcepts.Count > 1 ? $"等 {hitHotConcepts.Count} 个风口" : "")}");
        if (s.VolumeRatio > 0) parts.Add($"量比 {s.VolumeRatio:0.#} 放量");
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (s.Bbi > 0 && s.Close >= s.Bbi) parts.Add("站上 BBI 多头");
        if (s.MacdGoldenCross) parts.Add("MACD 金叉");
        if (parts.Count == 0) parts.Add("题材活跃、资金共识");
        return "题材动量：" + string.Join("，", parts) + "。右侧追强，注意题材退潮及时兑现。";
    }
}
