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
    /// 查询订单详情（含券商 tip 等完整字段），用于下单后状态确认。
    /// 返回 null 表示查询失败或订单不存在。
    /// </summary>
    Task<OrderResult?> GetOrderDetailAsync(string orderId);

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
    /// 券商返回的详细拒绝原因（如 tip 字段），成功时为空。
    /// </summary>
    public string? BrokerTip { get; set; }

    /// <summary>
    /// 订单状态
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// 是否被券商 OMS 拒绝（ret=201），可改单重试。
    /// </summary>
    public bool IsBrokerRejected { get; set; }

    /// <summary>
    /// 成交数量（股），仅查单返回。
    /// </summary>
    public long FilledVolume { get; set; }

    /// <summary>
    /// 订单状态中文文本（如"部分成交""全部成交"），仅查单返回。
    /// </summary>
    public string OrderStatusText { get; set; } = string.Empty;
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
