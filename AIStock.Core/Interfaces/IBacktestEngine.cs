using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 回测引擎接口
/// </summary>
public interface IBacktestEngine
{
    /// <summary>
    /// 运行回测
    /// </summary>
    Task<BacktestResult> RunBacktestAsync(BacktestConfig config, IStrategy strategy, List<string> codes, DateTime startTime, DateTime endTime);

    /// <summary>
    /// 运行单股票回测
    /// </summary>
    Task<BacktestResult> RunSingleStockBacktestAsync(BacktestConfig config, IStrategy strategy, string code, DateTime startTime, DateTime endTime);
}

/// <summary>
/// 回测配置
/// </summary>
public class BacktestConfig
{
    /// <summary>
    /// 初始资金
    /// </summary>
    public decimal InitialCapital { get; set; } = 1000000;

    /// <summary>
    /// 手续费率（%）
    /// </summary>
    public decimal CommissionRate { get; set; } = 0.03m;

    /// <summary>
    /// 印花税率（%）
    /// </summary>
    public decimal StampTaxRate { get; set; } = 0.1m;

    /// <summary>
    /// 滑点（%）
    /// </summary>
    public decimal Slippage { get; set; } = 0.1m;

    /// <summary>
    /// 冲击成本（%）
    /// </summary>
    public decimal ImpactCost { get; set; } = 0.05m;

    /// <summary>
    /// 单笔最大仓位比例（%）
    /// </summary>
    public decimal MaxPositionPercent { get; set; } = 10;

    /// <summary>
    /// 最大持仓数量
    /// </summary>
    public int MaxPositions { get; set; } = 10;

    /// <summary>
    /// 涨停板无法买入
    /// </summary>
    public bool EnforceLimitUp { get; set; } = true;

    /// <summary>
    /// 跌停板无法卖出
    /// </summary>
    public bool EnforceLimitDown { get; set; } = true;
}
