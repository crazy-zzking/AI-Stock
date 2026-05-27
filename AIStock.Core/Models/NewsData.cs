namespace AIStock.Core.Models;

/// <summary>
/// 新闻数据
/// </summary>
public class NewsData
{
    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 来源
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 原始URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 发布时间
    /// </summary>
    public DateTime PublishTime { get; set; }

    /// <summary>
    /// 内容摘要
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 全文内容
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 关联股票代码
    /// </summary>
    public List<string> RelatedStocks { get; set; } = new();

    /// <summary>
    /// 关联概念/板块
    /// </summary>
    public List<string> RelatedConcepts { get; set; } = new();

    /// <summary>
    /// 新闻类别（财经/政策/公司/行业）
    /// </summary>
    public string? Category { get; set; }
}
