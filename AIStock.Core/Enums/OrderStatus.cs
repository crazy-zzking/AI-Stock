namespace AIStock.Core.Enums;

/// <summary>
/// 订单状态
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// 待提交
    /// </summary>
    Pending,

    /// <summary>
    /// 已提交
    /// </summary>
    Submitted,

    /// <summary>
    /// 部分成交
    /// </summary>
    PartialFilled,

    /// <summary>
    /// 全部成交
    /// </summary>
    Filled,

    /// <summary>
    /// 已撤单
    /// </summary>
    Cancelled,

    /// <summary>
    /// 已拒绝
    /// </summary>
    Rejected,

    /// <summary>
    /// 失败
    /// </summary>
    Failed
}
