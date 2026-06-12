using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 小作文共振策略：知识星球"小作文"点名的个股 × 当日热门题材（板块正在炒作）双硬条件共振时进场。
/// 逻辑：小作文单独看是噪声（吹票/真消息混杂），板块炒作单独看不知道买哪只；
/// 两者交集 = 消息面有故事 + 场内资金正在这个方向打仗 → 故事最容易被兑现成涨幅的窗口。
/// 打分沿用题材因子集，另按小作文条数加成（多篇互相印证 > 单篇孤证）。
/// </summary>
public class WhisperThemeStrategy : WeightedSelectionStrategyBase
{
    /// <summary>每篇小作文的综合分加成（封顶 <see cref="MaxWhisperNotes"/> 篇）。</summary>
    private const decimal BonusPerNote = 4m;
    private const int MaxWhisperNotes = 3;

    public override string Key => StrategyKeys.Whisper;
    public override string Name => "小作文共振";
    public override string Description => "知识星球小作文点名 × 命中当日热门题材（板块炒作中）双条件共振进场";
    public override string PreferredRegime => "题材市 / 情绪活跃";

    /// <summary>小作文点名的票可能尚未活跃（埋伏价值最大的窗口），跳过活跃度粗筛全市场扫描。</summary>
    public override bool ScanFullUniverse => true;

    /// <summary>当前股票的小作文加成（ComputeFactors 暂存 → ComputeTotalScore 消费；Select 单线程逐股调用）。</summary>
    private decimal _whisperBonus;

    protected override bool PassesHardFilter(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionCriteria criteria, SelectionContext? ctx, MarketRegimeLevel level)
    {
        // 双硬条件：① 近 N 日有非负面小作文点名（利空小作文不构成做多依据）；② 命中当日热门题材（板块在炒，不是单票自嗨）
        if (ctx == null || !ctx.KnowledgeNotesByCode.TryGetValue(s.Code, out var notes)
            || !notes.Any(n => !n.IsNegative))
            return false;
        if (!SelectionScorers.HitsHotConcept(s.Code, ctx)) return false;

        // 防追高/排雷（沿用题材口径）
        if (s.Rsi > criteria.MaxRsi) return false;
        var maxRise = level == MarketRegimeLevel.Weak ? Math.Min(criteria.MaxRise20d, 30m) : criteria.MaxRise20d;
        if (s.Rise20d > maxRise) return false;
        if (seq.ConsecutiveLimitUp >= 3) return false;                     // 高位连板不接（小作文常出在情绪顶点）
        if (s.MainNetInflow < criteria.MinMainNetInflow) return false;     // 板块炒作要有资金承接
        if (SelectionScorers.IsExcludedTraditionalBigCap(s, criteria, ctx)) return false;
        if (criteria.RequireDragonTiger && dt == null) return false;
        return true;
    }

    protected override SelectionFactorScores ComputeFactors(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, DragonTigerEntity? dt,
        ActivityScreener.ActivityHit hit, SelectionContext? ctx, out List<string> hitHotConcepts)
    {
        // 加成 = Σ(每篇 × 可信度权重)，剔除负面，封顶 MaxWhisperNotes 篇——多篇高可信互证 > 单篇低可信孤证
        _whisperBonus = ctx != null && ctx.KnowledgeNotesByCode.TryGetValue(s.Code, out var notes)
            ? notes.Where(n => !n.IsNegative).Take(MaxWhisperNotes).Sum(n => BonusPerNote * n.BonusWeight)
            : 0m;

        return new()
        {
            Capital = SelectionScorers.Capital(s, seq),
            Technical = SelectionScorers.Technical(s),
            Position = SelectionScorers.Position(s),
            Form = SelectionScorers.Form(seq),
            DragonTiger = SelectionScorers.DragonTiger(dt),
            Activity = Math.Min(hit.ActivityScore, 100m),
            Theme = SelectionScorers.Theme(s.Code, ctx, out hitHotConcepts),
            Sector = SelectionScorers.Sector(s.Code, ctx),
            Volatility = SelectionScorers.Volatility(seq),
            RelativeStrength = SelectionScorers.RelativeStrength(s, ctx),
            News = SelectionScorers.News(s.Code, ctx),
        };
    }

    /// <summary>综合分 = 题材口径加权分 + 小作文条数加成（多篇印证更可信）。</summary>
    protected override decimal ComputeTotalScore(
        DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionFactorScores factors,
        SelectionWeights w, decimal scoreFactor, SelectionCriteria criteria)
        => Math.Clamp(base.ComputeTotalScore(s, seq, factors, w, scoreFactor, criteria) + _whisperBonus, 0m, 100m);

    // 与题材策略同口径：防高位连板接盘
    protected override decimal Penalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria)
        => SelectionScorers.LimitUpPenalty(s, seq, criteria);

    protected override List<string> BuildTags(
        DailyMarketSnapshotEntity s, DragonTigerEntity? dt, ActivityScreener.ActivityHit hit,
        SequenceFeatures seq, List<string> hitHotConcepts, SelectionFactorScores factors)
    {
        var tags = new List<string> { "小作文×风口" };
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}");
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
        var parts = new List<string> { "知识星球小作文点名" };
        if (hitHotConcepts.Count > 0)
            parts.Add($"且板块「{hitHotConcepts[0]}」正在炒作{(hitHotConcepts.Count > 1 ? $"（共命中 {hitHotConcepts.Count} 个风口）" : "")}");
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {SelectionScorers.FormatWan(s.MainNetInflow)}");
        if (seq.ConsecutiveLimitUp >= 1 || s.IsLimitUp) parts.Add("有涨停带动");
        return "小作文共振：" + string.Join("，", parts) + "。小作文真伪混杂，靠板块共振兑现，退潮或证伪即离场。";
    }
}
