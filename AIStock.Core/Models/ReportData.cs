namespace AIStock.Core.Models;

/// <summary>
/// 研报数据
/// </summary>
public class ReportData
{
    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 来源（东财/慧博等）
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
    /// 作者/机构
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// 关联股票代码
    /// </summary>
    public List<string> RelatedStocks { get; set; } = new();

    /// <summary>
    /// 内容摘要
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 全文内容
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 研报类型（个股/行业/策略/宏观）
    /// </summary>
    public string? ReportType { get; set; }

    /// <summary>
    /// 评级（买入/增持/中性/减持/卖出）
    /// </summary>
    public string? Rating { get; set; }

    /// <summary>
    /// 目标价
    /// </summary>
    public decimal? TargetPrice { get; set; }
}
