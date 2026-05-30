using AIStock.Monitor;
using Microsoft.Extensions.Options;

namespace AIStock.Web.Services;

/// <summary>
/// 监控后台服务 — 周期调用 PortfolioMonitorService 检查持仓/盈亏并告警。
/// </summary>
public class MonitorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MonitorOptions _options;
    private readonly ILogger<MonitorBackgroundService> _logger;

    public MonitorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MonitorOptions> options,
        ILogger<MonitorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("监控已禁用 (Monitor:Enabled=false)");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, _options.CheckIntervalSeconds));
        _logger.LogInformation("监控服务启动，检查间隔 {Interval}", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var monitor = scope.ServiceProvider.GetRequiredService<PortfolioMonitorService>();
                await monitor.CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "监控检查异常");
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
