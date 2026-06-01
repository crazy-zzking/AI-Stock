using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// Agent记忆实体 — 持久化Agent分析记录
/// </summary>
[Table("agent_memory")]
public class AgentMemoryEntity
{
    /// <summary>
    /// 主键ID
    /// </summary>
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// Agent ID
    /// </summary>
    [Column("agent_id")]
    [StringLength(50)]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// 任务类型
    /// </summary>
    [Column("task_type")]
    [StringLength(50)]
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 股票代码
    /// </summary>
    [Column("stock_code")]
    [StringLength(20)]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>
    /// 输入参数（JSON）
    /// </summary>
    [Column("input")]
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// 输出结果（JSON）
    /// </summary>
    [Column("output")]
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// 关键指标（JSON，仅数值字段）
    /// </summary>
    [Column("key_metrics")]
    public string? KeyMetrics { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    [Column("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    [Column("message")]
    [StringLength(500)]
    public string? Message { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    [Column("execution_time_ms")]
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
