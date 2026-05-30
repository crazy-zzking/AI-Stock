namespace AIStock.Worker.Scheduling;

/// <summary>
/// 单个任务的调度配置（Jobs:&lt;Name&gt;）。仅控制"何时跑/是否跑"，业务参数仍在各自的领域配置。
/// </summary>
public class JobOptions
{
    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动后是否立即跑一次（带轻微错峰）</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>周期间隔（秒）。&gt;0 时按间隔运行。</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>每日运行时刻（0-23，24制）。&gt;=0 时按每日定点运行（优先级高于 IntervalSeconds）。-1 表示不用。</summary>
    public int DailyAtHour { get; set; } = -1;
}

/// <summary>
/// 调度器配置：按任务名映射到各自的调度参数。
/// </summary>
public class JobSchedulerOptions
{
    public const string SectionName = "Jobs";

    public Dictionary<string, JobOptions> Items { get; set; } = new();

    public JobOptions For(string name) =>
        Items.TryGetValue(name, out var o) ? o : new JobOptions();
}
