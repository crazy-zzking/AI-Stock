using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 低吸埋伏策略（默认）。直接委托现有 <see cref="StockSelectionEngine"/>，
/// 与历史选股行为完全一致：规避追高，偏好低位/温和放量/回踩企稳的中小盘成长股（左侧）。
/// </summary>
public class LowDipStrategy : ISelectionStrategy
{
    private readonly StockSelectionEngine _engine;
    public LowDipStrategy(StockSelectionEngine engine) => _engine = engine;

    public string Key => StrategyKeys.LowDip;
    public string Name => "低吸埋伏";
    public string Description => "规避追高，偏好低位、温和放量、回踩企稳的中小盘成长股（左侧/埋伏）";
    public string PreferredRegime => "弱市 / 震荡市";

    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        IReadOnlyDictionary<string, SequenceFeatures> sequenceByCode,
        SelectionCriteria criteria,
        SelectionContext? context = null)
        => _engine.Select(activePool, dragonTigerByCode, sequenceByCode, criteria, context);
}
