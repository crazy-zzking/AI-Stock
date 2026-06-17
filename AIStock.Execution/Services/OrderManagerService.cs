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

            // 2. 股票级别暂停检查（连续失败/极端行情熔断）
            if (!_tradingGate.IsStockAllowed(request.Code, out var stockRejectReason))
            {
                return Rejected(stockRejectReason ?? "股票已被暂停交易", request);
            }

            // 3. kill-switch + 每日下单次数
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
                var dryRunId = $"DRYRUN-{Guid.NewGuid():N}";
                _logger.LogInformation(
                    "Order DryRun code={Code} side={Side} volume={Volume} price={Price} value={Value:N0} orderId={OrderId}",
                    request.Code, request.Side, request.Volume, request.Price, orderValue, dryRunId);
                return new OrderResult
                {
                    OrderId = dryRunId,
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

            _logger.LogInformation(
                "Order Live code={Code} side={Side} volume={Volume} price={Price} value={Value:N0} orderId={OrderId} status={Status} msg={Msg} brokerRejected={BrokerRejected}",
                request.Code, request.Side, request.Volume, request.Price, orderValue, orderId, status, result.Msg, result.IsFailed);

            var orderResult = new OrderResult
            {
                OrderId = orderId,
                Success = !result.IsFailed,
                Message = result.Msg,
                BrokerTip = result.BrokerTip,
                Status = status,
                IsBrokerRejected = result.IsFailed && !result.IsCompleted,
            };

            // 记录成功/失败，驱动连续失败熔断
            if (result.IsFailed)
                _tradingGate.RecordOrderFailure(request.Code);
            else
                _tradingGate.RecordOrderSuccess(request.Code);

            return orderResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order for {Code}", request.Code);
            _tradingGate.RecordOrderFailure(request.Code);
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
        var detail = await GetOrderDetailAsync(orderId);
        return detail?.Status ?? OrderStatus.Failed;
    }

    public async Task<OrderResult?> GetOrderDetailAsync(string orderId)
    {
        try
        {
            if (!long.TryParse(orderId, out var orderIdLong))
                return null;

            var provider = GetTradingProvider();
            if (provider == null)
                return null;

            var result = await provider.QueryOrderAsync(orderIdLong);
            if (result == null)
                return null;

            return new OrderResult
            {
                OrderId = result.OrderId.ToString(),
                Success = !result.IsFailed,
                Status = MapToOrderStatus(result),
                Message = result.Status,
                BrokerTip = result.BrokerTip,
                IsBrokerRejected = result.IsBrokerRejected,
                FilledVolume = result.FilledVolume,
                OrderStatusText = result.Status,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order detail for {OrderId}", orderId);
            return null;
        }
    }

    /// <summary>
    /// 将 Sanhu ret 码映射为统一 OrderStatus。
    /// 100=订单接受 / 101=正在处理 / 200=券商接受 / 201=券商拒绝
    /// 210=正在委托 / 211=部分成交 / 212=全部成交 / 213=已被撤单 / 3XX/4XX=错误
    /// </summary>
    private static OrderStatus MapToOrderStatus(TradingOrderStatus s)
    {
        return s.RetCode switch
        {
            100 or 200 => OrderStatus.Submitted,
            101 or 210 => OrderStatus.Pending,
            211 => OrderStatus.PartialFilled,
            212 => OrderStatus.Filled,
            213 => OrderStatus.Cancelled,
            201 => OrderStatus.Rejected,
            >= 300 => OrderStatus.Failed,
            _ => s.IsPending ? OrderStatus.Submitted : s.IsFailed ? OrderStatus.Failed : OrderStatus.Pending,
        };
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
