namespace AIStock.Prompt.Models;

/// <summary>
/// Prompt模板定义
/// </summary>
public class PromptTemplate
{
    /// <summary>
    /// Prompt名称（不含版本，如 "trend-analysis"）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 版本号（如 "v1", "v2"）
    /// </summary>
    public string Version { get; set; } = "v1";

    /// <summary>
    /// 分类（如 technical, macro, sentiment, strategy, risk）
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 推荐模型ID
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// 最大Token数
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 模板变量列表
    /// </summary>
    public List<string> Variables { get; set; } = new();

    /// <summary>
    /// 系统提示词模板
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 用户提示词模板（支持 {variable} 占位符）
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;
}
