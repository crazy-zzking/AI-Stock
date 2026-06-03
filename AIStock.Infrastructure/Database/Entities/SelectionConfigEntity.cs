using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 选股配置（版本化）。一条记录 = 一个版本的完整 SelectionCriteria（含权重）JSON 快照。
/// 同一 Name 可有多条不同 Version；IsActive=true 的那条为当前生效配置（每个 Name 至多一条生效）。
/// </summary>
[Table("selection_config")]
public class SelectionConfigEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>配置名（一套策略一个名，如"默认埋伏型"）</summary>
    [Column("name")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号（如 v1.0、v1.1，同 Name 下唯一）</summary>
    [Column("version")]
    [StringLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>完整配置 JSON（序列化的 SelectionCriteria，含 Weights）</summary>
    [Column("config_json", TypeName = "text")]
    public string ConfigJson { get; set; } = string.Empty;

    /// <summary>是否当前生效（每个 Name 至多一条为 true）</summary>
    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>备注（本次调参原因等）</summary>
    [Column("remark")]
    [StringLength(500)]
    public string? Remark { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
