namespace AIStock.Selection.Strategies;

/// <summary>
/// 策略提供者：统一对外提供"代码内置策略 + 数据库自建策略(strategy_definition)"的合并视图。
/// 取代直接注入 IEnumerable&lt;ISelectionStrategy&gt;，使前端新增的策略能在运行时被解析、列出、回测。
/// </summary>
public interface ISelectionStrategyProvider
{
    /// <summary>全部启用策略（内置在前，按 key 去重，内置优先）。</summary>
    Task<IReadOnlyList<ISelectionStrategy>> GetAllAsync(CancellationToken ct = default);

    /// <summary>按 key 解析策略；未知 key 回退低吸(lowdip)。</summary>
    Task<ISelectionStrategy> ResolveAsync(string? key, CancellationToken ct = default);
}
