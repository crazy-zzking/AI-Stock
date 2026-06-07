namespace AIStock.Core.Models;

/// <summary>
/// LLM模型配置
/// </summary>
public class LLMConfig
{
    /// <summary>
    /// 模型ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// API地址
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API密钥
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 优先级
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 最大Token数
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    public decimal? Temperature { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用思考模式（仅 api.deepseek.com 支持）
    /// </summary>
    public bool EnableThinking { get; set; }

    /// <summary>
    /// 思考 Token 预算（默认 8000，最小 1000）
    /// </summary>
    public int? ThinkingBudgetTokens { get; set; }

    /// <summary>
    /// 是否支持多模态（图片识别）
    /// </summary>
    public bool SupportsMultimodal { get; set; }
}
