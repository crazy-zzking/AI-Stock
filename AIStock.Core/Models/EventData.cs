namespace AIStock.Core.Models;

/// <summary>
/// 事件数据
/// </summary>
public class EventData
{
    /// <summary>
    /// 事件类型（news/report/policy/rumor）
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 来源
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 原始URL
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime EventTime { get; set; }

    /// <summary>
    /// 关联公司（公司名称 -> 股票代码）
    /// </summary>
    public Dictionary<string, string> RelatedCompanies { get; set; } = new();

    /// <summary>
    /// 关联产品/服务
    /// </summary>
    public List<string> RelatedProducts { get; set; } = new();

    /// <summary>
    /// 利好方向
    /// </summary>
    public List<string> PositiveDirections { get; set; } = new();

    /// <summary>
    /// 利空方向
    /// </summary>
    public List<string> NegativeDirections { get; set; } = new();

    /// <summary>
    /// 关联概念/板块
    /// </summary>
    public List<string> RelatedConcepts { get; set; } = new();
}
