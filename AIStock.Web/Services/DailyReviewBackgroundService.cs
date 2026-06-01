using AIStock.Core.Interfaces;

namespace AIStock.Web.Services;

/// <summary>
/// 每日复盘后台服务 — 收盘后（默认 17:00）在交易日触发 DailyReviewService 生成复盘报告并落库，
/// 同时把总结写入日志。报告内容（领涨板块/个股/归因/数据缺口）见 daily_review 表与 /api/review。
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
        _logger.LogInformation("每日复盘服务启动，将在交易日 {Hour}:00 生成复盘报告", ReviewHour);

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
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // 非交易日跳过
        var calendar = sp.GetRequiredService<ITradingCalendar>();
        if (!await calendar.IsTradingDayAsync(DateTime.Today, ct))
        {
            _logger.LogInformation("今日非交易日，跳过复盘");
            return;
        }

        var review = sp.GetRequiredService<DailyReviewService>();
        var report = await review.GenerateAndSaveAsync(DateTime.Today, ct);
        if (report != null)
            _logger.LogInformation("===== 每日复盘 ===== {Summary}", report.Summary);
    }

    private static TimeSpan NextReviewDelay()
    {
        var now = DateTime.Now;
        var todayReview = now.Date.AddHours(ReviewHour);
        var next = now < todayReview ? todayReview : todayReview.AddDays(1);
        return next - now;
    }
}
