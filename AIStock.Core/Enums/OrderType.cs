namespace AIStock.Core.Enums;

/// <summary>
/// 订单类型
/// </summary>
public enum OrderType
{
    /// <summary>
    /// 限价单
    /// </summary>
    Limit,

    /// <summary>
    /// 市价单
    /// </summary>
    Market,

    /// <summary>
    /// 止损单
    /// </summary>
    StopLoss,

    /// <summary>
    /// 止盈单
    /// </summary>
    TakeProfit,

    /// <summary>
    /// 止损限价单
    /// </summary>
    StopLimit
}
