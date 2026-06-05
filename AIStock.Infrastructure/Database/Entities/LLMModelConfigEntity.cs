using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// LLM模型配置实体
/// </summary>
[Table("llm_model_config")]
public class LLMModelConfigEntity
{
    /// <summary>
    /// 模型ID
    /// </summary>
    [Key]
    [Column("id")]
    [StringLength(50)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    [Column("name")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// API地址
    /// </summary>
    [Column("base_url")]
    [StringLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API密钥
    /// </summary>
    [Column("api_key")]
    [StringLength(500)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    [Column("model")]
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 优先级
    /// </summary>
    [Column("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    [Column("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 最大Token数
    /// </summary>
    [Column("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    [Column("temperature")]
    public decimal? Temperature { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [Column("description")]
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用思考模式（仅 api.deepseek.com 支持）
    /// </summary>
    [Column("enable_thinking")]
    public bool EnableThinking { get; set; }

    /// <summary>
    /// 思考 Token 预算
    /// </summary>
    [Column("thinking_budget_tokens")]
    public int? ThinkingBudgetTokens { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
