using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 用户自建选股策略定义（数据驱动）。一条记录 = 一个可配置策略的"口径"快照：
/// 过滤开关 + 因子口径选择 + 惩罚口径 + 文案模板（存 DefinitionJson）。
/// 权重/阈值仍走 selection_config（按 Key 关联）。内置策略(lowdip/trend/theme)不入此表。
/// </summary>
[Table("strategy_definition")]
public class StrategyDefinitionEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>策略键（小写，全局唯一，作为配置名/API 参数；不得与内置 key 冲突）</summary>
    [Column("strategy_key")]
    [StringLength(50)]
    public string Key { get; set; } = string.Empty;

    /// <summary>展示名</summary>
    [Column("name")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>一句话说明适用场景</summary>
    [Column("description")]
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>偏好的大盘环境（前端提示用）</summary>
    [Column("preferred_regime")]
    [StringLength(100)]
    public string PreferredRegime { get; set; } = string.Empty;

    /// <summary>是否启用（停用则不出现在策略池）</summary>
    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>策略定义 JSON（过滤开关 + 因子口径 + 惩罚口径 + 文案模板）</summary>
    [Column("definition_json", TypeName = "text")]
    public string DefinitionJson { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
