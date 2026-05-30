using AIStock.Core.Interfaces;
using AIStock.EventEngine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Services;

/// <summary>
/// 情报采集服务 — 采集新闻/研报，经 EventEngine（LLM 抽取）落库到 event_record。
/// </summary>
public class IntelligenceSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntelligenceSyncOptions _options;
    private readonly ILogger<IntelligenceSyncService> _logger;

    public IntelligenceSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<IntelligenceSyncOptions> options,
        ILogger<IntelligenceSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 采集新闻并处理入库。返回成功处理的条数。
    /// </summary>
    public async Task<int> SyncNewsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var collector = scope.ServiceProvider.GetRequiredService<INewsCollector>();
        var engine = scope.ServiceProvider.GetRequiredService<EventEngineService>();

        var newsList = await collector.CollectLatestNewsAsync(_options.NewsCount, ct);
        if (newsList.Count == 0)
        {
            _logger.LogWarning("未采集到新闻");
            return 0;
        }

        _logger.LogInformation("采集到 {Count} 条新闻，开始抽取入库", newsList.Count);
        var ok = 0;
        foreach (var news in newsList)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await engine.ProcessNewsAsync(news, ct);
                ok++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "新闻处理失败：{Title}", news.Title);
            }

            if (_options.ItemThrottleMs > 0)
                await Task.Delay(_options.ItemThrottleMs, ct);
        }

        _logger.LogInformation("新闻入库完成：{Ok}/{Total}", ok, newsList.Count);
        return ok;
    }

    /// <summary>
    /// 采集研报并处理入库。返回成功处理的条数。
    /// </summary>
    public async Task<int> SyncReportsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var collector = scope.ServiceProvider.GetRequiredService<IReportCollector>();
        var engine = scope.ServiceProvider.GetRequiredService<EventEngineService>();

        var reports = await collector.CollectReportsAsync(_options.ReportCount, ct);
        if (reports.Count == 0)
        {
            _logger.LogWarning("未采集到研报");
            return 0;
        }

        _logger.LogInformation("采集到 {Count} 篇研报，开始抽取入库", reports.Count);
        var ok = 0;
        foreach (var report in reports)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await engine.ProcessReportAsync(report, ct);
                ok++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "研报处理失败：{Title}", report.Title);
            }

            if (_options.ItemThrottleMs > 0)
                await Task.Delay(_options.ItemThrottleMs, ct);
        }

        _logger.LogInformation("研报入库完成：{Ok}/{Total}", ok, reports.Count);
        return ok;
    }
}
