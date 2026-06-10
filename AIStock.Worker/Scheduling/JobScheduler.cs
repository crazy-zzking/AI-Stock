using AIStock.Core.Interfaces;

namespace AIStock.Worker.Scheduling;

/// <summary>
/// 通用任务调度器 — 每个 IScheduledJob 独立循环、独立调度、独立异常隔离。
/// 调度参数（启停/周期/定点）每轮从 <see cref="IWorkerConfigProvider"/> 热读，前端改完无需重启即生效。
/// </summary>
public class JobScheduler : BackgroundService
{
    /// <summary>任务被禁用时的重新检查间隔（秒）——支持前端中途启用后自动起跑。</summary>
    private const int DisabledRecheckSeconds = 30;

    private readonly IEnumerable<IScheduledJob> _jobs;
    private readonly IWorkerConfigProvider _config;
    private readonly ILogger<JobScheduler> _logger;

    public JobScheduler(
        IEnumerable<IScheduledJob> jobs,
        IWorkerConfigProvider config,
        ILogger<JobScheduler> logger)
    {
        _jobs = jobs;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new List<Task>();
        var stagger = 0;
        foreach (var job in _jobs)
        {
            // 所有任务都起循环：是否真正执行由循环内的 Enabled 实时决定（支持热启停）
            loops.Add(RunJobLoopAsync(job, TimeSpan.FromSeconds(stagger), stoppingToken));
            stagger += 3; // 错峰启动，避免同时打外部接口
        }

        if (loops.Count == 0)
        {
            _logger.LogWarning("无后台任务");
            return;
        }

        await Task.WhenAll(loops);
    }

    private async Task RunJobLoopAsync(IScheduledJob job, TimeSpan startupStagger, CancellationToken ct)
    {
        if (!await DelaySafe(startupStagger, ct)) return; // 启动错峰
        var first = true;

        while (!ct.IsCancellationRequested)
        {
            var opt = await GetOptAsync(job.Name, ct);

            if (!opt.Enabled)
            {
                if (first) _logger.LogInformation("任务[{Name}]当前已禁用（可在前端启用）", job.Name);
                first = false;
                if (!await DelaySafe(TimeSpan.FromSeconds(DisabledRecheckSeconds), ct)) break;
                continue; // 重新检查，支持中途启用
            }

            // 首轮：非 RunOnStartup 则先等到下一个调度点再跑
            if (first)
            {
                first = false;
                _logger.LogInformation("任务[{Name}]已启动，调度：{Schedule}", job.Name, DescribeSchedule(job, opt));
                if (!opt.RunOnStartup)
                {
                    var delay0 = await job.GetNextDelayAsync(ct) ?? NextDelay(opt);
                    if (!await DelaySafe(delay0, ct)) break;
                    continue;
                }
                if (!await DelaySafe(TimeSpan.FromSeconds(5), ct)) break; // 让宿主稳定
            }

            if (ct.IsCancellationRequested) break;
            try
            {
                await job.ExecuteAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "任务[{Name}]执行异常", job.Name);
            }

            var delay = await job.GetNextDelayAsync(ct) ?? NextDelay(opt);
            if (!await DelaySafe(delay, ct)) break;
        }
    }

    /// <summary>从配置中心热读某任务当前调度参数（无则默认）。</summary>
    private async Task<JobOptions> GetOptAsync(string name, CancellationToken ct)
    {
        var jobs = await _config.GetAsync<Dictionary<string, JobOptions>>(JobSchedulerOptions.SectionName, ct);
        return jobs.TryGetValue(name, out var o) && o != null ? o : new JobOptions();
    }

    /// <summary>Task.Delay 包装：取消时返回 false（调用方据此退出循环）。</summary>
    private static async Task<bool> DelaySafe(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return !ct.IsCancellationRequested;
        try { await Task.Delay(delay, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>计算下次运行的等待时长</summary>
    private static TimeSpan NextDelay(JobOptions opt)
    {
        if (opt.DailyAtHour >= 0 && opt.DailyAtHour <= 23)
        {
            var minute = opt.DailyAtMinute is >= 0 and <= 59 ? opt.DailyAtMinute : 0;
            var now = DateTime.Now;
            var today = now.Date.AddHours(opt.DailyAtHour).AddMinutes(minute);
            var next = now < today ? today : today.AddDays(1);
            return next - now;
        }
        var seconds = opt.IntervalSeconds > 0 ? opt.IntervalSeconds : 3600;
        return TimeSpan.FromSeconds(seconds);
    }

    private static string DescribeSchedule(IScheduledJob job, JobOptions opt) =>
        job.HasDynamicSchedule ? "动态间隔（由任务自定）"
        : opt.DailyAtHour >= 0 ? $"每日 {opt.DailyAtHour}:{(opt.DailyAtMinute is >= 0 and <= 59 ? opt.DailyAtMinute : 0):D2}"
        : $"每 {(opt.IntervalSeconds > 0 ? opt.IntervalSeconds : 3600)} 秒";
}
