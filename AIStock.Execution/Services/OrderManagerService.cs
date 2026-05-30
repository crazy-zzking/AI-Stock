using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 订单管理实现 - 通过IDataProvider接口解耦。
/// 所有下单路径（自主决策/手动API/Worker）均经此处，安全护栏在此统一强制执行。
/// </summary>
public class OrderManagerService : IOrderManager
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ITradingGate _tradingGate;
    private readonly TradingGuardOptions _guard;
    private readonly ILogger<OrderManagerService> _logger;

    public OrderManagerService(
        IDataProviderResolver dataProviderResolver,
        ITradingGate tradingGate,
        Microsoft.Extensions.Options.IOptions<TradingGuardOptions> guardOptions,
        ILogger<OrderManagerService> logger)
    {
        _dataProviderResolver = dataProviderResolver;
        _tradingGate = tradingGate;
        _guard = guardOptions.Value;
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

            // ===== 安全护栏 =====
            // 1. 单笔金额上限
            var orderValue = request.Price * request.Volume;
            if (_guard.MaxOrderValue > 0 && orderValue > _guard.MaxOrderValue)
            {
                return Rejected($"单笔金额 {orderValue:N0} 超过上限 {_guard.MaxOrderValue:N0}", request);
            }

            // 2. kill-switch + 每日下单次数
            if (!_tradingGate.TryReserveOrderSlot(out var rejectReason))
            {
                return Rejected(rejectReason ?? "交易闸门拒绝", request);
            }

            // 3. 下单前账户校验（买入查可用资金，卖出查可用持仓）
            var accountCheck = await CheckAccountAsync(provider, request, orderValue);
            if (accountCheck != null)
            {
                return accountCheck;
            }

            // 4. DryRun 模式：不调用券商接口，仅记录意向单
            if (_tradingGate.Mode == TradingMode.DryRun)
            {
                _logger.LogInformation(
                    "[DryRun] 模拟下单 {Side} {Code} {Volume}@{Price} (金额 {Value:N0})，未发送至券商",
                    request.Side, request.Code, request.Volume, request.Price, orderValue);
                return new OrderResult
                {
                    OrderId = $"DRYRUN-{Guid.NewGuid():N}",
                    Success = true,
                    Message = "[DryRun] 模拟下单成功，未真实成交",
                    Status = OrderStatus.Submitted
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

    private OrderResult Rejected(string reason, OrderRequest request)
    {
        _logger.LogWarning("下单被护栏拒绝 {Code} {Side} {Volume}@{Price}: {Reason}",
            request.Code, request.Side, request.Volume, request.Price, reason);
        return new OrderResult
        {
            Success = false,
            Message = $"下单被拒绝: {reason}",
            Status = OrderStatus.Failed
        };
    }

    /// <summary>
    /// 下单前账户校验。通过返回 null，不通过返回拒绝结果。
    /// </summary>
    private async Task<OrderResult?> CheckAccountAsync(IDataProvider provider, OrderRequest request, decimal orderValue)
    {
        AccountInfo account;
        try
        {
            account = await provider.GetAccountInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下单前账户查询失败，保守拒单 {Code}", request.Code);
            return Rejected("账户查询失败，保守拒单", request);
        }

        if (request.Side.ToLower() == "buy")
        {
            // 原子预留：将在途未结买单累计金额计入，防止并行批量下单叠加超配
            if (!_tradingGate.TryReserveBuyValue(account.AvailableBalance, orderValue, out var reason))
            {
                return Rejected(reason ?? "买入敞口校验未通过", request);
            }
        }
        else
        {
            var position = account.Positions.FirstOrDefault(p => p.Code == request.Code);
            var available = position?.AvailableVolume ?? 0;
            if (available < request.Volume)
            {
                return Rejected(
                    $"可用持仓不足: 需卖 {request.Volume}，可用 {available}", request);
            }
        }

        return null;
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
