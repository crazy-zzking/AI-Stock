using AIStock.Worker.Services;
using Microsoft.Extensions.Options;

namespace AIStock.Worker;

/// <summary>
/// 数据同步后台服务 — 交易日收盘后同步；启动时按库内最新K线日期判断是否补拉。
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
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var closeHour = Math.Clamp(_options.SyncHour, 0, 23);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 检查库内最新K线日期 vs 最近已收盘交易日，落后才同步（启动补拉同理）
                var (need, last, target) = await _syncService.ShouldSyncKlinesAsync(closeHour, stoppingToken);
                if (need)
                {
                    _logger.LogInformation("需同步：库内最新K线 {Last}，目标交易日 {Target}",
                        last?.ToString("yyyy-MM-dd") ?? "无", target.ToString("yyyy-MM-dd"));
                    await RunSyncCycleAsync(stoppingToken);
                }
                else
                {
                    _logger.LogInformation("数据已最新（截至 {Last}），等待下次收盘后同步", last?.ToString("yyyy-MM-dd"));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "同步调度异常"); }

            // 睡到下一个收盘时刻（今日未到则今日，否则明日）；醒来再判定是否交易日/是否需要同步
            var delay = TimeUntilNextClose(closeHour);
            _logger.LogInformation("下次检查在 {Delay} 后（约 {Time}）", delay, DateTime.Now.Add(delay).ToString("MM-dd HH:mm"));
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>距离下一个收盘时刻的时长</summary>
    private static TimeSpan TimeUntilNextClose(int closeHour)
    {
        var now = DateTime.Now;
        var todayClose = now.Date.AddHours(closeHour);
        var next = now < todayClose ? todayClose : todayClose.AddDays(1);
        return next - now;
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
