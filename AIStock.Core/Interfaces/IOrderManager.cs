using AIStock.Core.Enums;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 订单管理接口
/// </summary>
public interface IOrderManager
{
    /// <summary>
    /// 下单
    /// </summary>
    Task<OrderResult> PlaceOrderAsync(OrderRequest request);

    /// <summary>
    /// 撤单
    /// </summary>
    Task<bool> CancelOrderAsync(string orderId);

    /// <summary>
    /// 查询订单状态
    /// </summary>
    Task<OrderStatus> GetOrderStatusAsync(string orderId);

    /// <summary>
    /// 获取订单列表
    /// </summary>
    Task<List<OrderInfo>> GetOrdersAsync(DateTime startTime, DateTime endTime);
}

/// <summary>
/// 订单请求
/// </summary>
public class OrderRequest
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 买卖方向（buy/sell）
    /// </summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// 订单类型
    /// </summary>
    public OrderType OrderType { get; set; }

    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 策略名称
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// 信号ID
    /// </summary>
    public string SignalId { get; set; } = string.Empty;
}

/// <summary>
/// 订单结果
/// </summary>
public class OrderResult
{
    /// <summary>
    /// 订单ID
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 订单状态
    /// </summary>
    public OrderStatus Status { get; set; }
}

/// <summary>
/// 订单信息
/// </summary>
public class OrderInfo
{
    /// <summary>
    /// 订单ID
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 买卖方向
    /// </summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 成交数量
    /// </summary>
    public long FilledVolume { get; set; }

    /// <summary>
    /// 成交价格
    /// </summary>
    public decimal FilledPrice { get; set; }

    /// <summary>
    /// 订单状态
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateTime { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdateTime { get; set; }
}
