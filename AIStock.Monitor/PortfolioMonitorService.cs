using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Monitor;

/// <summary>
/// 组合监控 — 拉取实时持仓/盈亏，按阈值产生告警。
/// </summary>
public class PortfolioMonitorService
{
    private readonly IPositionManager _positionManager;
    private readonly IAlertNotifier _notifier;
    private readonly MonitorOptions _options;
    private readonly ILogger<PortfolioMonitorService> _logger;

    public PortfolioMonitorService(
        IPositionManager positionManager,
        IAlertNotifier notifier,
        IOptions<MonitorOptions> options,
        ILogger<PortfolioMonitorService> logger)
    {
        _positionManager = positionManager;
        _notifier = notifier;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 执行一次监控检查。返回触发的告警数。
    /// </summary>
    public async Task<int> CheckOnceAsync(CancellationToken ct = default)
    {
        var summary = await _positionManager.GetPositionSummaryAsync();
        var alerts = new List<Alert>();

        // 账户整体盈亏
        if (summary.TotalProfitRate <= _options.AccountLossPercent)
        {
            alerts.Add(new Alert(AlertLevel.Critical, "账户亏损预警",
                $"账户总盈亏率 {summary.TotalProfitRate:F2}% 已触及阈值 {_options.AccountLossPercent:F2}%，" +
                $"总资产 {summary.TotalAssets:N0}，总盈亏 {summary.TotalProfit:N0}"));
        }

        // 仓位比例
        if (summary.TotalAssets > 0)
        {
            var positionRatio = summary.PositionValue / summary.TotalAssets * 100;
            if (positionRatio >= _options.MaxPositionRatioPercent)
            {
                alerts.Add(new Alert(AlertLevel.Warning, "仓位过重预警",
                    $"持仓占总资产 {positionRatio:F2}%，已触及阈值 {_options.MaxPositionRatioPercent:F2}%"));
            }
        }

        // 单只持仓亏损
        foreach (var p in summary.Positions)
        {
            if (p.ProfitRate <= _options.PositionLossPercent)
            {
                alerts.Add(new Alert(AlertLevel.Warning, "个股亏损预警",
                    $"{p.Name}({p.Code}) 盈亏率 {p.ProfitRate:F2}% 已触及阈值 {_options.PositionLossPercent:F2}%，" +
                    $"市值 {p.MarketValue:N0}，盈亏 {p.Profit:N0}"));
            }
        }

        foreach (var alert in alerts)
            await _notifier.SendAsync(alert, ct);

        if (alerts.Count == 0)
            _logger.LogDebug("监控检查正常：总盈亏率 {Rate:F2}%，持仓 {Count} 只", summary.TotalProfitRate, summary.PositionCount);

        return alerts.Count;
    }
}
