namespace AIStock.Core.Models;

/// <summary>
/// LLM响应
/// </summary>
public class LLMResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 响应内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 使用的模型ID
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Token使用量
    /// </summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 响应时间（毫秒）
    /// </summary>
    public long ResponseTimeMs { get; set; }

    /// <summary>
    /// 思考过程（DeepSeek reasoning_content，仅思考模式下返回）
    /// </summary>
    public string? ThinkingContent { get; set; }
}

/// <summary>
/// Token使用量
/// </summary>
public class TokenUsage
{
    /// <summary>
    /// 输入Token数
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// 输出Token数
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// 总Token数
    /// </summary>
    public int TotalTokens { get; set; }
}
