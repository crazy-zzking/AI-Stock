using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 情绪分析接口
/// </summary>
public interface ISentimentAnalyzer
{
    /// <summary>
    /// 分析文本情绪
    /// </summary>
    Task<SentimentResult> AnalyzeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析事件情绪
    /// </summary>
    Task<SentimentResult> AnalyzeEventAsync(EventData eventData, CancellationToken cancellationToken = default);
}
