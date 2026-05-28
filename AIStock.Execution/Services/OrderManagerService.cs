using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 订单管理实现
/// </summary>
public class OrderManagerService : IOrderManager
{
    private readonly ILogger<OrderManagerService> _logger;
    private readonly Dictionary<string, OrderInfo> _orders = new();

    public OrderManagerService(ILogger<OrderManagerService> logger)
    {
        _logger = logger;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request)
    {
        try
        {
            var orderId = Guid.NewGuid().ToString("N");

            var order = new OrderInfo
            {
                OrderId = orderId,
                Code = request.Code,
                Side = request.Side,
                Price = request.Price,
                Volume = request.Volume,
                Status = OrderStatus.Submitted,
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow
            };

            _orders[orderId] = order;

            _logger.LogInformation("Order placed: {OrderId} {Side} {Code} {Volume}@{Price}",
                orderId, request.Side, request.Code, request.Volume, request.Price);

            return new OrderResult
            {
                OrderId = orderId,
                Success = true,
                Message = "订单已提交",
                Status = OrderStatus.Submitted
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order for {Code}", request.Code);
            return new OrderResult
            {
                Success = false,
                Message = $"下单失败: {ex.Message}",
                Status = OrderStatus.Failed
            };
        }
    }

    public async Task<bool> CancelOrderAsync(string orderId)
    {
        if (!_orders.ContainsKey(orderId))
        {
            _logger.LogWarning("Order not found: {OrderId}", orderId);
            return false;
        }

        var order = _orders[orderId];
        if (order.Status == OrderStatus.Filled)
        {
            _logger.LogWarning("Cannot cancel filled order: {OrderId}", orderId);
            return false;
        }

        order.Status = OrderStatus.Cancelled;
        order.UpdateTime = DateTime.UtcNow;

        _logger.LogInformation("Order cancelled: {OrderId}", orderId);
        return true;
    }

    public async Task<OrderStatus> GetOrderStatusAsync(string orderId)
    {
        if (!_orders.ContainsKey(orderId))
        {
            return OrderStatus.Failed;
        }

        return _orders[orderId].Status;
    }

    public async Task<List<OrderInfo>> GetOrdersAsync(DateTime startTime, DateTime endTime)
    {
        return _orders.Values
            .Where(o => o.CreateTime >= startTime && o.CreateTime <= endTime)
            .OrderByDescending(o => o.CreateTime)
            .ToList();
    }
}
