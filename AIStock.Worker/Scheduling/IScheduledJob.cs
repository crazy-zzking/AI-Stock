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
}
