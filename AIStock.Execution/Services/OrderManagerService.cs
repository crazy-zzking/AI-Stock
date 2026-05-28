using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 订单管理实现 - 通过IDataProvider接口解耦
/// </summary>
public class OrderManagerService : IOrderManager
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<OrderManagerService> _logger;

    public OrderManagerService(
        IDataProviderResolver dataProviderResolver,
        ILogger<OrderManagerService> logger)
    {
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    private IDataProvider? GetTradingProvider()
    {
        return _dataProviderResolver.GetPrimaryProvider(DataCapability.Trading);
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request)
    {
        try
        {
            var provider = GetTradingProvider();
            if (provider == null)
            {
                return new OrderResult
                {
                    Success = false,
                    Message = "交易Provider未配置",
                    Status = OrderStatus.Failed
                };
            }

            if (request.Volume < 100)
            {
                return new OrderResult
                {
                    Success = false,
                    Message = "下单数量不足1手(100股)",
                    Status = OrderStatus.Failed
                };
            }

            TradingOrderResult result;
            if (request.Side.ToLower() == "buy")
            {
                result = await provider.PlaceBuyOrderAsync(request.Code, request.Price, (int)request.Volume);
            }
            else
            {
                result = await provider.PlaceSellOrderAsync(request.Code, request.Price, (int)request.Volume);
            }

            var orderId = result.OrderId.ToString();
            var status = result.IsAccepted ? OrderStatus.Submitted :
                         result.IsCompleted ? OrderStatus.Filled :
                         result.IsFailed ? OrderStatus.Failed :
                         OrderStatus.Pending;

            return new OrderResult
            {
                OrderId = orderId,
                Success = !result.IsFailed,
                Message = result.Msg,
                Status = status
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
        _logger.LogWarning("CancelOrder not supported via current trading provider");
        return false;
    }

    public async Task<OrderStatus> GetOrderStatusAsync(string orderId)
    {
        try
        {
            if (!long.TryParse(orderId, out var orderIdLong))
                return OrderStatus.Failed;

            var provider = GetTradingProvider();
            if (provider == null)
                return OrderStatus.Failed;

            var result = await provider.QueryOrderAsync(orderIdLong);
            if (result == null)
                return OrderStatus.Failed;

            if (result.IsCompleted) return OrderStatus.Filled;
            if (result.IsFailed) return OrderStatus.Failed;
            if (result.IsPending) return OrderStatus.Submitted;
            return OrderStatus.Pending;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order status for {OrderId}", orderId);
            return OrderStatus.Failed;
        }
    }

    public async Task<List<OrderInfo>> GetOrdersAsync(DateTime startTime, DateTime endTime)
    {
        try
        {
            var provider = GetTradingProvider();
            if (provider == null)
                return new List<OrderInfo>();

            var trades = await provider.GetTodayTradesAsync();
            return trades.Select(t => new OrderInfo
            {
                OrderId = t.AgreeId.ToString(),
                Code = t.Code,
                Side = t.Type,
                Price = t.Price,
                Volume = t.Volume,
                Status = OrderStatus.Filled,
                CreateTime = t.TradeTime,
                UpdateTime = t.TradeTime
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get orders");
            return new List<OrderInfo>();
        }
    }
}
