namespace AIStock.Core.Enums;

/// <summary>
/// 交易信号类型
/// </summary>
public enum SignalType
{
    /// <summary>
    /// 买入
    /// </summary>
    Buy,

    /// <summary>
    /// 卖出
    /// </summary>
    Sell,

    /// <summary>
    /// 持有
    /// </summary>
    Hold,

    /// <summary>
    /// 强力买入
    /// </summary>
    StrongBuy,

    /// <summary>
    /// 强力卖出
    /// </summary>
    StrongSell,

    /// <summary>
    /// 止损卖出
    /// </summary>
    StopLoss,

    /// <summary>
    /// 止盈卖出
    /// </summary>
    TakeProfit,
}
