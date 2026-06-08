using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.EventEngine.Services;
using AIStock.Intelligence;
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
        var ksOptions = scope.ServiceProvider.GetRequiredService<IOptions<KnowledgeStarOptions>>().Value;

        var items = await collector.GetLatestContentAsync(20, ct);
        if (items.Count == 0)
        {
            _logger.LogInformation("知识星球无新内容（或未配置 token/groups）");
            return 0;
        }

        // 增量过滤：只处理比上次水位线更新的主题；无水位线（首跑）按回看窗口兜底。
        // 高频调度下靠此过滤避免重复 LLM 花费（URL 去重保留作双保险）。
        var watermark = await engine.GetLatestKnowledgeStarTimeAsync(ct);
        var lookbackHours = Math.Max(1, ksOptions.LookbackHours);
        var cutoff = watermark ?? DateTime.Now.AddHours(-lookbackHours);
        var before = items.Count;
        items.RemoveAll(x => x.PublishTime <= cutoff);
        _logger.LogInformation("知识星球增量过滤：水位线={Watermark} 截断={Cutoff} 保留 {Kept}/{Before} 条",
            watermark?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(无)", cutoff.ToString("yyyy-MM-dd HH:mm:ss"), items.Count, before);
        if (items.Count == 0)
        {
            _logger.LogInformation("知识星球本轮无增量主题");
            return 0;
        }

        // 既无正文也无图片的才丢弃；纯图片帖保留走多模态识别
        items.RemoveAll(x => string.IsNullOrWhiteSpace(x.Content) && x.ImageUrls.Count == 0);
        var ok = 0;
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (string.IsNullOrWhiteSpace(item.Content) && item.ImageUrls.Count == 0) continue;
                if (await engine.ExistsByUrlAsync(item.Url, ct)) continue; // 去重前置，省 LLM

                EssayAnalysisResult result;
                if (item.ImageUrls.Count > 0)
                {
                    // 图片帖：一次性把全部图片 + 配文交给多模态模型识别
                    result = await essay.AnalyzeImagesAsync(item.ImageUrls, item.Content, ct);

                    // 纯图片帖把识别文本回填正文，避免入库记录为空、便于检索
                    if (string.IsNullOrWhiteSpace(item.Content) && !string.IsNullOrWhiteSpace(result.ParsedContent))
                    {
                        item.Content = result.ParsedContent;
                        if (string.IsNullOrWhiteSpace(item.Title))
                            item.Title = result.ParsedContent.Length > 40 ? result.ParsedContent[..40] : result.ParsedContent;
                    }
                }
                else
                {
                    result = await essay.AnalyzeTextAsync(item.Content, ct);
                }

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
