using AIStock.Core.Interfaces;
using AIStock.Monitor;
using Microsoft.Extensions.Options;

namespace AIStock.Web.Services;

/// <summary>
/// 监控后台服务 — 仅 A 股交易时段周期检查持仓/盈亏并告警。
/// </summary>
public class MonitorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITradingCalendar _tradingCalendar;
    private readonly MonitorOptions _options;
    private readonly ILogger<MonitorBackgroundService> _logger;

    private static readonly TimeSpan MorningStart = new(9, 30, 0);
    private static readonly TimeSpan MorningEnd = new(11, 30, 0);
    private static readonly TimeSpan AfternoonStart = new(13, 0, 0);
    private static readonly TimeSpan AfternoonEnd = new(15, 0, 0);

    public MonitorBackgroundService(
        IServiceScopeFactory scopeFactory,
        ITradingCalendar tradingCalendar,
        IOptions<MonitorOptions> options,
        ILogger<MonitorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _tradingCalendar = tradingCalendar;
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
        _logger.LogInformation("监控服务启动，检查间隔 {Interval}（仅 A 股交易日交易时段生效）", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await IsTradingTimeAsync(stoppingToken))
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

    /// <summary>
    /// 判断是否为 A 股交易日且处于连续竞价时段（9:30-11:30, 13:00-15:00）
    /// </summary>
    private async Task<bool> IsTradingTimeAsync(CancellationToken ct)
    {
        try
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));

            if (!await _tradingCalendar.IsTradingDayAsync(now.Date, ct))
                return false;

            var t = now.TimeOfDay;
            return (t >= MorningStart && t <= MorningEnd)
                || (t >= AfternoonStart && t <= AfternoonEnd);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check trading time, assuming non-trading");
            return false;
        }
    }
}
