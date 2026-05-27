namespace AIStock.Core.Models;

/// <summary>
/// 研报分析结果
/// </summary>
public class ReportAnalysis
{
    /// <summary>
    /// 研报摘要
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 超预期点
    /// </summary>
    public List<string> ExceedExpectations { get; set; } = new();

    /// <summary>
    /// 低于预期点
    /// </summary>
    public List<string> BelowExpectations { get; set; } = new();

    /// <summary>
    /// 产业方向
    /// </summary>
    public List<string> IndustryDirections { get; set; } = new();

    /// <summary>
    /// 关联概念/板块
    /// </summary>
    public List<string> RelatedConcepts { get; set; } = new();

    /// <summary>
    /// 核心观点
    /// </summary>
    public string? CoreView { get; set; }

    /// <summary>
    /// 风险提示
    /// </summary>
    public List<string> Risks { get; set; } = new();

    /// <summary>
    /// 投资建议
    /// </summary>
    public string? InvestmentAdvice { get; set; }
}
