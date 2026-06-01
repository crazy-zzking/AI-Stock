using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// Prompt模板配置实体 — 存数据库，支持前端CRUD
/// </summary>
[Table("prompt_template")]
public class PromptTemplateEntity
{
    /// <summary>
    /// Prompt名称（如 trend-analysis）
    /// </summary>
    [Column("name")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 版本号（如 v1, v2）
    /// </summary>
    [Column("version")]
    [StringLength(20)]
    public string Version { get; set; } = "v1";

    /// <summary>
    /// 分类（technical/macro/sentiment/strategy/risk）
    /// </summary>
    [Column("category")]
    [StringLength(50)]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    [Column("description")]
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 推荐模型ID
    /// </summary>
    [Column("model")]
    [StringLength(50)]
    public string? Model { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    [Column("temperature")]
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// 最大Token数
    /// </summary>
    [Column("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 模板变量列表（JSON数组）
    /// </summary>
    [Column("variables")]
    [StringLength(2000)]
    public string? Variables { get; set; }

    /// <summary>
    /// 系统提示词模板
    /// </summary>
    [Column("system_prompt")]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 用户提示词模板（支持 {variable} 占位符）
    /// </summary>
    [Column("user_prompt")]
    public string UserPrompt { get; set; } = string.Empty;

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
