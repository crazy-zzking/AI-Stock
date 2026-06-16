using Microsoft.Extensions.Options;

namespace AIStock.Worker.Scheduling;

/// <summary>
/// 通用任务调度器 — 每个 IScheduledJob 独立循环、独立调度、独立异常隔离。
/// </summary>
public class JobScheduler : BackgroundService
{
    private readonly IEnumerable<IScheduledJob> _jobs;
    private readonly JobSchedulerOptions _options;
    private readonly ILogger<JobScheduler> _logger;

    public JobScheduler(
        IEnumerable<IScheduledJob> jobs,
        IOptions<JobSchedulerOptions> options,
        ILogger<JobScheduler> logger)
    {
        _jobs = jobs;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new List<Task>();
        var stagger = 0;
        foreach (var job in _jobs)
        {
            var opt = _options.For(job.Name);
            if (!opt.Enabled)
            {
                _logger.LogInformation("任务[{Name}]已禁用", job.Name);
                continue;
            }
            loops.Add(RunJobLoopAsync(job, opt, TimeSpan.FromSeconds(stagger), stoppingToken));
            stagger += 3; // 错峰启动，避免同时打外部接口
        }

        if (loops.Count == 0)
        {
            _logger.LogWarning("无启用的后台任务");
            return;
        }

        await Task.WhenAll(loops);
    }

    private async Task RunJobLoopAsync(IScheduledJob job, JobOptions opt, TimeSpan startupStagger, CancellationToken ct)
    {
        // 初始等待：启动跑则错峰后立即跑，否则等到下一个调度点
        var initial = opt.RunOnStartup ? startupStagger.Add(TimeSpan.FromSeconds(5)) : NextDelay(opt);
        try { await Task.Delay(initial, ct); } catch (OperationCanceledException) { return; }

        _logger.LogInformation("任务[{Name}]已启动，调度：{Schedule}", job.Name, DescribeSchedule(opt));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await job.ExecuteAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "任务[{Name}]执行异常", job.Name);
            }

            var delay = NextDelay(opt);
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>计算下次运行的等待时长</summary>
    private static TimeSpan NextDelay(JobOptions opt)
    {
        if (opt.DailyAtHour >= 0 && opt.DailyAtHour <= 23)
        {
            var now = DateTime.Now;
            var today = now.Date.AddHours(opt.DailyAtHour);
            var next = now < today ? today : today.AddDays(1);
            return next - now;
        }
        var seconds = opt.IntervalSeconds > 0 ? opt.IntervalSeconds : 3600;
        return TimeSpan.FromSeconds(seconds);
    }

    private static string DescribeSchedule(JobOptions opt) =>
        opt.DailyAtHour >= 0 ? $"每日 {opt.DailyAtHour}:00"
        : $"每 {(opt.IntervalSeconds > 0 ? opt.IntervalSeconds : 3600)} 秒";
}
