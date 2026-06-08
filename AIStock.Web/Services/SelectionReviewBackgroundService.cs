using AIStock.Selection.Review;

namespace AIStock.Web.Services;

/// <summary>
/// 选股 LLM 复评后台消费服务。从 <see cref="SelectionReviewQueue"/> 取批次 id，
/// 逐批调 <see cref="SelectionReviewService"/> 做异步复评（每批独立 scope，互不阻塞选股请求）。
/// </summary>
public class SelectionReviewBackgroundService : BackgroundService
{
    private readonly SelectionReviewQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SelectionReviewBackgroundService> _logger;

    public SelectionReviewBackgroundService(
        SelectionReviewQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SelectionReviewBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("选股 LLM 复评后台服务启动，等待选股批次入队");

        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var review = scope.ServiceProvider.GetRequiredService<SelectionReviewService>();
                    await review.ReviewBatchAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "选股复评后台处理批次 {Id} 异常", id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 主机停机，正常退出
        }

        _logger.LogInformation("选股 LLM 复评后台服务已停止");
    }
}
