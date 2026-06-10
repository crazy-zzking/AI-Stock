using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// Worker 任务配置（前端可配置）。一行 = 一个配置段（section）的完整 JSON 快照，
/// 形状与原 appsettings 对应段一致。Worker 与 Web 共用此表：Web 读写、Worker 按 TTL 热读。
/// section 取值：Jobs / DataSync / IntelligenceSync / MarketSnapshot / KnowledgeStar / GraphPromotion。
/// </summary>
[Table("worker_config")]
public class WorkerConfigEntity
{
    /// <summary>配置段名（主键，与 appsettings 段名一致）</summary>
    [Key]
    [Column("section")]
    [StringLength(100)]
    public string Section { get; set; } = string.Empty;

    /// <summary>该段完整配置 JSON</summary>
    [Column("config_json", TypeName = "text")]
    public string ConfigJson { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
