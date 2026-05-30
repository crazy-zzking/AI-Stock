using AIStock.Worker.Services;
using Microsoft.Extensions.Options;

namespace AIStock.Worker;

/// <summary>
/// 数据同步后台服务 — 按配置周期同步股票池与日K到数据库。
/// </summary>
public class Worker : BackgroundService
{
    private readonly DataSyncService _syncService;
    private readonly DataSyncOptions _options;
    private readonly ILogger<Worker> _logger;

    public Worker(
        DataSyncService syncService,
        IOptions<DataSyncOptions> options,
        ILogger<Worker> logger)
    {
        _syncService = syncService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("数据同步已禁用 (DataSync:Enabled=false)，Worker 空转");
            return;
        }

        // 启动时延迟片刻，等待数据源 Provider 就绪
        if (_options.RunOnStartup)
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var interval = TimeSpan.FromHours(Math.Max(1, _options.IntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSyncCycleAsync(stoppingToken);

            _logger.LogInformation("下一轮数据同步将在 {Interval} 后执行", interval);
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

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("开始数据同步周期");
            await _syncService.SyncStockBaseAsync(ct);

            if (_options.SyncDetails)
                await _syncService.SyncStockDetailsAsync(ct);

            if (_options.SyncKlines)
                await _syncService.SyncKlinesAsync(ct);

            _logger.LogInformation("数据同步周期完成");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据同步周期异常");
        }
    }
}
