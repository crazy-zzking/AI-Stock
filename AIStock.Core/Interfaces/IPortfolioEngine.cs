using AIStock.Core.Enums;
using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 组合管理接口
/// </summary>
public interface IPortfolioEngine
{
    /// <summary>
    /// 计算目标仓位
    /// </summary>
    Task<List<PortfolioPosition>> CalculateTargetPositionsAsync(List<TradeSignal> signals, decimal totalCapital);

    /// <summary>
    /// 再平衡
    /// </summary>
    Task<List<TradeSignal>> RebalanceAsync(List<PortfolioPosition> currentPositions, List<PortfolioPosition> targetPositions);

    /// <summary>
    /// 计算仓位权重
    /// </summary>
    Task<Dictionary<string, decimal>> CalculateWeightsAsync(List<TradeSignal> signals, PositionSizeMode mode);
}
