namespace AIStock.Core.Enums;

/// <summary>
/// 止损模式
/// </summary>
public enum StopLossMode
{
    /// <summary>
    /// ATR止损
    /// </summary>
    ATR,

    /// <summary>
    /// 固定止损
    /// </summary>
    Fixed,

    /// <summary>
    /// 动态止盈
    /// </summary>
    Trailing,

    /// <summary>
    /// 无止损
    /// </summary>
    None
}
