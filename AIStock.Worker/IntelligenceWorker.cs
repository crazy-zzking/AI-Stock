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
            try
            {
                _logger.LogInformation("开始情报采集周期");
                if (_options.CollectNews)
                    await _syncService.SyncNewsAsync(stoppingToken);
                if (_options.CollectReports)
                    await _syncService.SyncReportsAsync(stoppingToken);
                _logger.LogInformation("情报采集周期完成");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "情报采集周期异常");
            }

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
}
