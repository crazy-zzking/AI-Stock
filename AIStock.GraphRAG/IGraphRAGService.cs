namespace AIStock.GraphRAG;

/// <summary>
/// 图谱增强RAG服务 — 将知识图谱数据注入LLM分析上下文
/// </summary>
public interface IGraphRAGService
{
    /// <summary>
    /// 为指定股票构建知识图谱上下文
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <param name="queryType">查询类型（可选，如"analysis"/"risk"/"sentiment"）</param>
    Task<GraphContext> BuildContextAsync(string code, string? queryType = null);
}

/// <summary>
/// 图谱上下文 — 注入到LLM Prompt中的结构化知识
/// </summary>
public class GraphContext
{
    /// <summary>
    /// 公司基本信息摘要
    /// </summary>
    public string? CompanyInfo { get; set; }

    /// <summary>
    /// 供应商关系描述
    /// </summary>
    public string? Suppliers { get; set; }

    /// <summary>
    /// 客户关系描述
    /// </summary>
    public string? Customers { get; set; }

    /// <summary>
    /// 产业链位置描述
    /// </summary>
    public string? IndustryChain { get; set; }

    /// <summary>
    /// 相关事件描述
    /// </summary>
    public string? RelatedEvents { get; set; }

    /// <summary>
    /// 关系路径（间接关联的公司链）
    /// </summary>
    public string? RelationPaths { get; set; }

    /// <summary>
    /// 格式化为 LLM 可读的纯文本上下文
    /// </summary>
    public string ToPromptText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(CompanyInfo)) parts.Add($"公司信息：{CompanyInfo}");
        if (!string.IsNullOrEmpty(Suppliers)) parts.Add($"供应商关系：{Suppliers}");
        if (!string.IsNullOrEmpty(Customers)) parts.Add($"客户关系：{Customers}");
        if (!string.IsNullOrEmpty(IndustryChain)) parts.Add($"产业链位置：{IndustryChain}");
        if (!string.IsNullOrEmpty(RelatedEvents)) parts.Add($"相关事件：{RelatedEvents}");
        if (!string.IsNullOrEmpty(RelationPaths)) parts.Add($"关系路径：{RelationPaths}");
        return parts.Count > 0 ? string.Join("\n", parts) : "无额外图谱上下文";
    }
}
