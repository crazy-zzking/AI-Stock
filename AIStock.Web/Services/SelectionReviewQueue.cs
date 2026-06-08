using System.Threading.Channels;
using AIStock.Selection.Review;

namespace AIStock.Web.Services;

/// <summary>
/// 选股复评队列的 Channel 实现（单例）。选股完成后入队批次 id，
/// 由 <see cref="SelectionReviewBackgroundService"/> 异步消费。无界队列、丢弃重复无害（消费端幂等）。
/// </summary>
public sealed class SelectionReviewQueue : ISelectionReviewQueue
{
    private readonly Channel<long> _channel =
        Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ILogger<SelectionReviewQueue> _logger;

    public SelectionReviewQueue(ILogger<SelectionReviewQueue> logger) => _logger = logger;

    public void Enqueue(long selectionResultId)
    {
        if (!_channel.Writer.TryWrite(selectionResultId))
            _logger.LogWarning("选股复评入队失败：批次 {Id}", selectionResultId);
        else
            _logger.LogInformation("选股复评已入队：批次 {Id}", selectionResultId);
    }

    public ChannelReader<long> Reader => _channel.Reader;
}
