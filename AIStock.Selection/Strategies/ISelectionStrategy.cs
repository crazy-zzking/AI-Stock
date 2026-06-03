using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 选股策略接口。市场状态决定启用哪些策略，每个策略在同一活跃池上用各自的
/// 过滤口径 + 因子侧重独立选股。打分复用 <see cref="SelectionScorers"/>，口径不漂移。
/// </summary>
public interface ISelectionStrategy
{
    /// <summary>策略键（小写，作为配置中心的配置名 / API 参数）。</summary>
    string Key { get; }
    /// <summary>展示名。</summary>
    string Name { get; }
    /// <summary>一句话说明适用场景。</summary>
    string Description { get; }
    /// <summary>该策略偏好的大盘环境（前端提示用）。</summary>
    string PreferredRegime { get; }

    List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        IReadOnlyDictionary<string, SequenceFeatures> sequenceByCode,
        SelectionCriteria criteria,
        SelectionContext? context = null);
}

/// <summary>策略键常量。</summary>
public static class StrategyKeys
{
    public const string LowDip = "lowdip";
    public const string Trend = "trend";
    public const string Theme = "theme";
}
