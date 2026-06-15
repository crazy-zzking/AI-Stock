namespace AIStock.Core.Models;

/// <summary>
/// 小作文分析结果
/// </summary>
public class EssayAnalysisResult : AnalysisResultBase
{
    /// <summary>
    /// 原始内容
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;

    /// <summary>
    /// 解析后的内容（OCR/ASR结果）
    /// </summary>
    public string ParsedContent { get; set; } = string.Empty;

    /// <summary>
    /// 内容类型（text/image/audio）
    /// </summary>
    public string ContentType { get; set; } = "text";

    /// <summary>
    /// 可信度评分（0-100）
    /// </summary>
    public int CredibilityScore { get; set; }

    /// <summary>
    /// 情绪倾向（positive/negative/neutral）
    /// </summary>
    public string Sentiment { get; set; } = "neutral";

    /// <summary>
    /// 情绪分数（-1到1）
    /// </summary>
    public decimal SentimentScore { get; set; }

    /// <summary>
    /// 关联公司
    /// </summary>
    public List<string> RelatedCompanies { get; set; } = new();

    /// <summary>
    /// 关联概念
    /// </summary>
    public List<string> RelatedConcepts { get; set; } = new();

    /// <summary>
    /// 关键信息摘要
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 提炼标题（图片帖由识别内容提炼，纯图片帖入库标题用）
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 风险提示
    /// </summary>
    public List<string> RiskWarnings { get; set; } = new();

    /// <summary>
    /// 分析结论
    /// </summary>
    public string Conclusion { get; set; } = string.Empty;
}

/// <summary>
/// 知识星球内容
/// </summary>
public class KnowledgeStarContent
{
    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 作者
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// 发布时间
    /// </summary>
    public DateTime PublishTime { get; set; }

    /// <summary>
    /// 来源URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 关联股票
    /// </summary>
    public List<string> RelatedStocks { get; set; } = new();

    /// <summary>
    /// 关联概念
    /// </summary>
    public List<string> RelatedConcepts { get; set; } = new();

    /// <summary>
    /// 内容类型（text/image/link）
    /// </summary>
    public string ContentType { get; set; } = "text";

    /// <summary>
    /// 图片URL列表
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();
}
