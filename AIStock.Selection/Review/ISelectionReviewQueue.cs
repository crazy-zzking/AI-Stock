using Microsoft.Extensions.Logging;

namespace AIStock.Selection.Review;

/// <summary>
/// 选股复评队列：选股完成后把批次 id 入队，由后台服务异步消费复评。
/// 选股层只依赖此接口；具体 Channel 实现 + 后台消费在 Web 层。
/// </summary>
public interface ISelectionReviewQueue
{
    /// <summary>把某次选股批次（selection_result.id）排入复评队列。</summary>
    void Enqueue(long selectionResultId);
}

/// <summary>默认空实现（未启用异步复评时使用，仅记日志不处理）。</summary>
public sealed class NullSelectionReviewQueue : ISelectionReviewQueue
{
    private readonly ILogger<NullSelectionReviewQueue> _logger;
    public NullSelectionReviewQueue(ILogger<NullSelectionReviewQueue> logger) => _logger = logger;
    public void Enqueue(long selectionResultId)
        => _logger.LogDebug("复评队列未启用，忽略批次 {Id}", selectionResultId);
}
