using AIStock.Core.Models;

namespace AIStock.Selection;

/// <summary>
/// 出手闸门（纯函数）。在打分排序之后、产出 TOP-N 之前统一裁决"出不出手、出几只"：
/// 弱市收紧综合分下限并压缩出手数量，风险释放日更严（默认仅留 1 只、可配置为完全空仓）。
/// 中性/强市默认零改动（下限取 <see cref="SelectionWeights.MinScore"/>，数量不压缩），向后兼容。
/// 旧引擎(<see cref="StockSelectionEngine"/>)与加权策略基类共用此口径，避免择时逻辑漂移。
/// </summary>
public static class RegimeGate
{
    /// <summary>
    /// 对已按综合分降序排好的候选施加闸门，返回最终入选列表。
    /// </summary>
    /// <param name="orderedByScoreDesc">已按 TotalScore 降序排列的候选。</param>
    /// <param name="criteria">选股条件（含 TopN 与 Weights 里的闸门参数）。</param>
    /// <param name="level">大盘环境等级（弱/中/强）。</param>
    /// <param name="kind">市场状态类型（风险释放优先于弱市判定）。</param>
    public static List<StockSelectionResult> Apply(
        IEnumerable<StockSelectionResult> orderedByScoreDesc,
        SelectionCriteria criteria,
        MarketRegimeLevel level,
        RegimeKind kind)
    {
        var w = criteria.Weights ?? new SelectionWeights();

        decimal floor;
        int cap;
        if (kind == RegimeKind.RiskOff)
        {
            floor = w.RegimeRiskOffMinScore;
            cap = w.RegimeRiskOffTopNCap;
        }
        else if (level == MarketRegimeLevel.Weak)
        {
            floor = w.RegimeWeakMinScore;
            cap = w.RegimeWeakTopNCap;
        }
        else
        {
            floor = w.MinScore;
            cap = int.MaxValue;
        }

        var n = Math.Min(criteria.TopN, Math.Max(0, cap));

        return orderedByScoreDesc
            .Where(r => r.TotalScore >= floor)
            .Take(n)
            .ToList();
    }
}
