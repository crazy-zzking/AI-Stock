namespace AIStock.Core.Models;

/// <summary>
/// LLM请求
/// </summary>
public class LLMRequest
{
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 用户提示词
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 最大Token数（覆盖配置）
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 温度参数（覆盖配置）
    /// </summary>
    public decimal? Temperature { get; set; }

    /// <summary>
    /// 是否流式输出
    /// </summary>
    public bool Stream { get; set; } = false;

    /// <summary>
    /// 指定模型ID（可选，为空则使用默认模型）
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// 图片URL列表（多模态识别用，非空时以 OpenAI vision 格式随 user 消息一并发送）
    /// </summary>
    public List<string>? ImageUrls { get; set; }
}
