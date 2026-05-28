using AIStock.Core.Enums;
using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 止损系统接口
/// </summary>
public interface IStopLossSystem
{
    /// <summary>
    /// 计算止损价
    /// </summary>
    decimal CalculateStopLoss(TradeSignal signal, List<KlineData> klines, StopLossMode mode);

    /// <summary>
    /// 计算止盈价
    /// </summary>
    decimal CalculateTakeProfit(TradeSignal signal, List<KlineData> klines, StopLossMode mode);

    /// <summary>
    /// ATR止损
    /// </summary>
    decimal CalculateATRStopLoss(decimal entryPrice, decimal atr, decimal multiplier = 2);

    /// <summary>
    /// 固定止损
    /// </summary>
    decimal CalculateFixedStopLoss(decimal entryPrice, decimal stopPercent);

    /// <summary>
    /// 动态止盈
    /// </summary>
    decimal CalculateTrailingStop(decimal highestPrice, decimal trailingPercent);
}
