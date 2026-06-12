using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// Worker 任务运行态（每任务一行）。Worker 写入运行开始/结束/耗时，Web 读取供前端展示；
/// Web 也可置 <see cref="RunRequested"/> 请求手动「立即运行」，由 Worker 轮询消费。
/// </summary>
[Table("worker_job_run")]
public class WorkerJobRunEntity
{
    /// <summary>任务名（主键，与 IScheduledJob.Name 一致）</summary>
    [Key]
    [Column("job_name")]
    [StringLength(64)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>是否正在运行（单飞锁持有中）</summary>
    [Column("is_running")]
    public bool IsRunning { get; set; }

    /// <summary>最近一次开始运行时间</summary>
    [Column("last_start")]
    public DateTime? LastStart { get; set; }

    /// <summary>最近一次结束时间</summary>
    [Column("last_end")]
    public DateTime? LastEnd { get; set; }

    /// <summary>最近一次耗时（毫秒）</summary>
    [Column("last_duration_ms")]
    public long? LastDurationMs { get; set; }

    /// <summary>最近一次触发来源：Scheduled / Manual</summary>
    [Column("last_trigger")]
    [StringLength(16)]
    public string? LastTrigger { get; set; }

    /// <summary>最近一次是否成功（无异常）</summary>
    [Column("last_success")]
    public bool? LastSuccess { get; set; }

    /// <summary>最近一次错误信息（失败时）</summary>
    [Column("last_error")]
    [StringLength(500)]
    public string? LastError { get; set; }

    /// <summary>是否有待处理的手动运行请求（Web 置位，Worker 消费时清零）</summary>
    [Column("run_requested")]
    public bool RunRequested { get; set; }

    /// <summary>手动运行请求时间</summary>
    [Column("requested_at")]
    public DateTime? RequestedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
