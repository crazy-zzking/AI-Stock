using System.Collections.Concurrent;
using System.Diagnostics;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Worker.Scheduling;

/// <summary>
/// 任务运行协调器（单例）。统一所有任务的执行入口：
/// 1) 单飞锁——同一任务禁止并发（调度触发与手动「立即运行」互斥，抢不到锁即跳过）；
/// 2) 运行态落库——开始/结束/耗时/成功失败写入 worker_job_run，供 Web 前端展示。
/// </summary>
public class JobRunCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobRunCoordinator> _logger;
    private readonly IReadOnlyDictionary<string, IScheduledJob> _jobs;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public JobRunCoordinator(
        IEnumerable<IScheduledJob> jobs,
        IServiceScopeFactory scopeFactory,
        ILogger<JobRunCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var map = new Dictionary<string, IScheduledJob>();
        foreach (var j in jobs) map[j.Name] = j; // 同名以最后注册为准
        _jobs = map;
    }

    public IScheduledJob? Find(string name) => _jobs.TryGetValue(name, out var j) ? j : null;

    /// <summary>按任务名执行（手动触发用）。未知任务返回 false。</summary>
    public Task<bool> TryRunByNameAsync(string name, string trigger, CancellationToken ct)
    {
        var job = Find(name);
        if (job == null)
        {
            _logger.LogWarning("手动运行请求的任务[{Name}]不存在", name);
            return Task.FromResult(false);
        }
        return TryRunAsync(job, trigger, ct);
    }

    /// <summary>
    /// 执行一次任务：抢到单飞锁才跑（否则跳过，返回 false），全程把运行态落库。
    /// </summary>
    public async Task<bool> TryRunAsync(IScheduledJob job, string trigger, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(job.Name, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(0, ct))
        {
            _logger.LogInformation("任务[{Name}]正在运行，跳过本次{Trigger}触发", job.Name, trigger);
            return false;
        }

        var sw = Stopwatch.StartNew();
        await MarkStartAsync(job.Name, trigger, ct);
        try
        {
            await job.ExecuteAsync(ct);
            sw.Stop();
            await MarkEndAsync(job.Name, sw.ElapsedMilliseconds, true, null, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await MarkEndAsync(job.Name, sw.ElapsedMilliseconds, false, "已取消", CancellationToken.None);
            throw; // 让调度循环据此退出
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "任务[{Name}]执行异常", job.Name);
            await MarkEndAsync(job.Name, sw.ElapsedMilliseconds, false, ex.Message, CancellationToken.None);
            return true; // 已执行（失败），非"跳过"
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>启动时清零所有残留的 is_running（防 Worker 崩溃后卡死为"运行中"）。</summary>
    public async Task ResetRunningFlagsAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var running = await db.WorkerJobRun.Where(r => r.IsRunning).ToListAsync(ct);
            foreach (var r in running) r.IsRunning = false;
            if (running.Count > 0) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清零任务运行态失败（忽略）");
        }
    }

    private async Task MarkStartAsync(string name, string trigger, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var row = await GetOrCreateAsync(db, name, ct);
            row.IsRunning = true;
            row.LastStart = DateTime.Now;
            row.LastTrigger = trigger;
            row.RunRequested = false; // 消费掉手动请求
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写任务[{Name}]开始态失败（忽略，不影响执行）", name);
        }
    }

    private async Task MarkEndAsync(string name, long durationMs, bool success, string? error, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var row = await GetOrCreateAsync(db, name, ct);
            row.IsRunning = false;
            row.LastEnd = DateTime.Now;
            row.LastDurationMs = durationMs;
            row.LastSuccess = success;
            row.LastError = error == null ? null : (error.Length > 500 ? error[..500] : error);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写任务[{Name}]结束态失败（忽略）", name);
        }
    }

    private static async Task<WorkerJobRunEntity> GetOrCreateAsync(AIStockDbContext db, string name, CancellationToken ct)
    {
        var row = await db.WorkerJobRun.FirstOrDefaultAsync(r => r.JobName == name, ct);
        if (row == null)
        {
            row = new WorkerJobRunEntity { JobName = name };
            db.WorkerJobRun.Add(row);
        }
        return row;
    }
}
