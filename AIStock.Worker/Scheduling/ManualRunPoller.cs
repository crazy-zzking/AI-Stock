using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Worker.Scheduling;

/// <summary>
/// 手动「立即运行」轮询器。每 ~3 秒扫 worker_job_run 中 run_requested=true 的任务，
/// 交给 <see cref="JobRunCoordinator"/> 执行（并发由协调器单飞锁拦截）。
/// 用 DB 轮询触发（而非 Redis），保证 Redis 未配置时也可用。
/// </summary>
public class ManualRunPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly JobRunCoordinator _coordinator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ManualRunPoller> _logger;

    public ManualRunPoller(
        JobRunCoordinator coordinator,
        IServiceScopeFactory scopeFactory,
        ILogger<ManualRunPoller> logger)
    {
        _coordinator = coordinator;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "手动运行轮询异常（忽略本轮）");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        List<string> names;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            names = await db.WorkerJobRun
                .Where(r => r.RunRequested)
                .Select(r => r.JobName)
                .ToListAsync(ct);
        }
        if (names.Count == 0) return;

        foreach (var name in names)
        {
            _logger.LogInformation("收到任务[{Name}]立即运行请求", name);
            // 不 await：让单个任务后台跑，轮询继续；并发由协调器单飞锁拦截。
            // 传 ct（应用停止令牌）：手动任务在 Worker 关闭时一并优雅取消。
            _ = _coordinator.TryRunByNameAsync(name, "Manual", ct);
        }
    }
}
