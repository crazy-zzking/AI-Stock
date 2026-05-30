using AIStock.Worker.Services;
using Microsoft.Extensions.Options;

namespace AIStock.Worker;

/// <summary>
/// 情报采集后台服务 — 按配置周期采集新闻/研报并抽取入库。
/// </summary>
public class IntelligenceWorker : BackgroundService
{
    private readonly IntelligenceSyncService _syncService;
    private readonly IntelligenceSyncOptions _options;
    private readonly ILogger<IntelligenceWorker> _logger;

    public IntelligenceWorker(
        IntelligenceSyncService syncService,
        IOptions<IntelligenceSyncOptions> options,
        ILogger<IntelligenceWorker> logger)
    {
        _syncService = syncService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("情报采集已禁用 (IntelligenceSync:Enabled=false)");
            return;
        }

        if (_options.RunOnStartup)
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("开始情报采集周期");
            // 三源并行，各自独立异常隔离（各 Sync 内部使用独立 scope/DbContext）
            var tasks = new List<Task>();
            if (_options.CollectNews)
                tasks.Add(RunSafeAsync("新闻", _syncService.SyncNewsAsync, stoppingToken));
            if (_options.CollectAnnouncements)
                tasks.Add(RunSafeAsync("公告", _syncService.SyncAnnouncementsAsync, stoppingToken));
            if (_options.CollectReports)
                tasks.Add(RunSafeAsync("研报", _syncService.SyncReportsAsync, stoppingToken));

            await Task.WhenAll(tasks);
            if (stoppingToken.IsCancellationRequested) break;
            _logger.LogInformation("情报采集周期完成");

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>运行单个采集源，异常隔离（不影响其他源）</summary>
    private async Task RunSafeAsync(string name, Func<CancellationToken, Task<int>> action, CancellationToken ct)
    {
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException)
        {
            // 取消由上层处理
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "情报采集源[{Name}]异常", name);
        }
    }
}
