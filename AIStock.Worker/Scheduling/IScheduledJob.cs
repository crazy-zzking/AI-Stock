namespace AIStock.Worker.Scheduling;

/// <summary>
/// 可调度后台任务。每个任务独立启停、独立调度、独立异常隔离。
/// </summary>
public interface IScheduledJob
{
    /// <summary>任务名（与配置 Jobs:&lt;Name&gt; 对应，唯一）</summary>
    string Name { get; }

    /// <summary>执行一次任务</summary>
    Task ExecuteAsync(CancellationToken ct);

    /// <summary>是否使用自定义调度间隔（仅用于日志描述）。</summary>
    bool HasDynamicSchedule => false;

    /// <summary>自定义下次运行延迟（异步，可查交易日历）；返回 null 用调度器默认固定间隔。</summary>
    Task<TimeSpan?> GetNextDelayAsync(CancellationToken ct) => Task.FromResult<TimeSpan?>(null);
}
