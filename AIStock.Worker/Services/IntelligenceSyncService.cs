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
    /// 采集公告并处理入库。仅标题含利好/利空关键字的公告才送 LLM 分析（省 token）。
    /// </summary>
    public async Task<int> SyncAnnouncementsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var collector = scope.ServiceProvider.GetRequiredService<INewsCollector>();
        var engine = scope.ServiceProvider.GetRequiredService<EventEngineService>();

        var list = await collector.CollectLatestAnnouncementsAsync(_options.AnnouncementCount, ct);
        if (list.Count == 0)
        {
            _logger.LogWarning("未采集到公告");
            return 0;
        }

        var keywords = _options.AnnouncementKeywords ?? new List<string>();
        var filtered = keywords.Count == 0
            ? list
            : list.Where(a => keywords.Any(k => a.Title.Contains(k))).ToList();

        _logger.LogInformation("采集到 {Total} 条公告，命中关键字 {Hit} 条，开始抽取入库", list.Count, filtered.Count);
        var ok = 0;
        foreach (var ann in filtered)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await engine.ProcessNewsAsync(ann, ct);
                ok++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "公告处理失败：{Title}", ann.Title);
            }

            if (_options.ItemThrottleMs > 0)
                await Task.Delay(_options.ItemThrottleMs, ct);
        }

        _logger.LogInformation("公告入库完成：{Ok}/{Hit}（共采集 {Total}）", ok, filtered.Count, list.Count);
        return ok;
    }

    /// <summary>
    /// 采集知识星球内容并经小作文可信度分析后入库。采集前按 URL 去重，避免重复 LLM 分析。
    /// </summary>
    public async Task<int> SyncKnowledgeStarAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var collector = scope.ServiceProvider.GetRequiredService<IKnowledgeStarCollector>();
        var essay = scope.ServiceProvider.GetRequiredService<IEssayAnalyzer>();
        var engine = scope.ServiceProvider.GetRequiredService<EventEngineService>();

        var items = await collector.GetLatestContentAsync(20, ct);
        if (items.Count == 0)
        {
            _logger.LogInformation("知识星球无新内容（或未配置 token/groups）");
            return 0;
        }
        items.RemoveAll(x => x.ContentType == "text" && x.Content == "");
        var ok = 0;
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;
                if (await engine.ExistsByUrlAsync(item.Url, ct)) continue; // 去重前置，省 LLM

                var result = await essay.AnalyzeTextAsync(item.Content, ct);
                var saved = await engine.SaveKnowledgeStarEventAsync(item, result, ct);
                if (saved != null) ok++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "知识星球内容处理失败：{Title}", item.Title);
            }

            if (_options.ItemThrottleMs > 0)
                await Task.Delay(_options.ItemThrottleMs, ct);
        }

        _logger.LogInformation("知识星球入库完成：{Ok}/{Total}", ok, items.Count);
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
