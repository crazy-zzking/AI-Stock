namespace AIStock.Core.Models;

/// <summary>
/// 情绪分析结果
/// </summary>
public class SentimentResult
{
    /// <summary>
    /// 情绪类型（positive/negative/neutral）
    /// </summary>
    public string Sentiment { get; set; } = "neutral";

    /// <summary>
    /// 情绪分数（-1到1）
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// 置信度（0到1）
    /// </summary>
    public decimal Confidence { get; set; }

    /// <summary>
    /// 关键词
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 分析理由
    /// </summary>
    public string? Reason { get; set; }
}
