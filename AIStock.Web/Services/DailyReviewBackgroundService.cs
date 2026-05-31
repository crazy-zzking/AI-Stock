using AIStock.Core.Interfaces;

namespace AIStock.Web.Services;

/// <summary>
/// 每日交易复盘服务 — 收盘后（默认 17:00）汇总当日订单统计并写入日志，用于灰度阶段人工复盘。
/// </summary>
public class DailyReviewBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyReviewBackgroundService> _logger;

    // 收盘复盘时刻：默认 17:00（A股 15:00 收盘，留出数据同步时间）
    private const int ReviewHour = 17;

    public DailyReviewBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DailyReviewBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("每日复盘服务启动，将在 {Hour}:00 输出日报", ReviewHour);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(NextReviewDelay(), stoppingToken);
                await RunDailyReviewAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "每日复盘执行异常");
            }
        }
    }

    private async Task RunDailyReviewAsync(CancellationToken ct)
    {
        var today = DateTime.Today;
        var startTime = today;
        var endTime = today.AddDays(1).AddSeconds(-1);

        using var scope = _scopeFactory.CreateScope();
        var orderManager = scope.ServiceProvider.GetRequiredService<IOrderManager>();
        var tradingGate = scope.ServiceProvider.GetRequiredService<ITradingGate>();

        try
        {
            var orders = await orderManager.GetOrdersAsync(startTime, endTime);

            var totalOrders = orders.Count;
            var successOrders = orders.Count(o => o.Status == Core.Enums.OrderStatus.Filled ||
                                                   o.Status == Core.Enums.OrderStatus.Submitted);
            var failedOrders = orders.Count(o => o.Status == Core.Enums.OrderStatus.Failed);
            var buyOrders = orders.Count(o => o.Side?.ToLower() == "buy");
            var sellOrders = orders.Count(o => o.Side?.ToLower() == "sell");
            var totalValue = orders.Sum(o => o.Price * o.Volume);

            _logger.LogInformation(
                "===== 每日复盘 {Date} ===== " +
                "mode={Mode} totalOrders={Total} success={Success} failed={Failed} " +
                "buy={Buy} sell={Sell} totalValue={Value:N0} gateTodayCount={GateCount}",
                today.ToString("yyyy-MM-dd"),
                tradingGate.Mode,
                totalOrders, successOrders, failedOrders,
                buyOrders, sellOrders, totalValue,
                tradingGate.TodayOrderCount);

            if (failedOrders > 0)
            {
                _logger.LogWarning("今日有 {FailedCount} 笔订单失败，请检查日志排查原因", failedOrders);
            }

            if (totalOrders == 0)
            {
                _logger.LogInformation("今日无任何下单记录（DryRun模式或无信号触发）");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "每日复盘数据获取失败");
        }
    }

    private static TimeSpan NextReviewDelay()
    {
        var now = DateTime.Now;
        var todayReview = now.Date.AddHours(ReviewHour);
        var next = now < todayReview ? todayReview : todayReview.AddDays(1);
        return next - now;
    }
}
