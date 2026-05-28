using AIStock.Core.Enums;
using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 仓位管理接口
/// </summary>
public interface IPositionSizer
{
    /// <summary>
    /// 计算仓位大小
    /// </summary>
    decimal CalculatePositionSize(TradeSignal signal, decimal totalCapital, PositionSizeMode mode);

    /// <summary>
    /// Kelly公式计算
    /// </summary>
    decimal CalculateKelly(decimal winRate, decimal profitLossRatio);

    /// <summary>
    /// 风险平价计算
    /// </summary>
    decimal CalculateRiskParity(decimal volatility, decimal targetRisk);

    /// <summary>
    /// 波动率目标计算
    /// </summary>
    decimal CalculateVolatilityTarget(decimal volatility, decimal targetVolatility, decimal totalCapital);
}
