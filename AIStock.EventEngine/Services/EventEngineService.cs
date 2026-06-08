using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.EventEngine.Services;

/// <summary>
/// 事件引擎服务
/// </summary>
public class EventEngineService
{
    private readonly AIStockDbContext _dbContext;
    private readonly IEventExtractor _eventExtractor;
    private readonly ISentimentAnalyzer _sentimentAnalyzer;
    private readonly IIntensityScorer _intensityScorer;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<EventEngineService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>A 股代码：6 位纯数字。非此格式的标识视为股票名，需反查代码。</summary>
    private static readonly Regex _stockCodeRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    public EventEngineService(
        AIStockDbContext dbContext,
        IEventExtractor eventExtractor,
        ISentimentAnalyzer sentimentAnalyzer,
        IIntensityScorer intensityScorer,
        IMessageBus messageBus,
        ILogger<EventEngineService> logger)
    {
        _dbContext = dbContext;
        _eventExtractor = eventExtractor;
        _sentimentAnalyzer = sentimentAnalyzer;
        _intensityScorer = intensityScorer;
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <summary>
    /// 处理研报事件
    /// </summary>
    public async Task<EventRecordEntity> ProcessReportAsync(ReportData report, CancellationToken cancellationToken = default)
    {
        try
        {
            // 0. 去重：同 Url 已处理过则跳过
            if (!string.IsNullOrEmpty(report.Url))
            {
                var existing = await _dbContext.EventRecord
                    .FirstOrDefaultAsync(e => e.Url == report.Url, cancellationToken);
                if (existing != null)
                {
                    _logger.LogDebug("跳过重复研报：{Title}", report.Title);
                    return existing;
                }
            }

            // 1. 抽取事件
            var eventData = await _eventExtractor.ExtractFromReportAsync(report, cancellationToken);

            // 2+3. 情绪分析与强度评分并行（均只依赖 eventData，彼此独立）
            var sentimentTask = _sentimentAnalyzer.AnalyzeEventAsync(eventData, cancellationToken);
            var intensityTask = _intensityScorer.ScoreAsync(eventData, cancellationToken);
            await Task.WhenAll(sentimentTask, intensityTask);
            var sentiment = sentimentTask.Result;
            var intensity = intensityTask.Result;

            // 4. 保存事件记录
            var eventRecord = new EventRecordEntity
            {
                EventType = "report",
                Title = report.Title,
                Content = report.Content ?? report.Summary,
                Source = report.Source,
                Url = report.Url,
                Sentiment = sentiment.Sentiment,
                SentimentScore = sentiment.Score,
                Importance = intensity.Importance,
                Credibility = intensity.Credibility,
                RelatedStocks = string.Join(",", eventData.RelatedCompanies.Select(x=>x.Value).Concat(report.RelatedStocks)
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                RelatedConcepts = string.Join(",", eventData.RelatedConcepts),
                LLMAnalysis = JsonSerializer.Serialize(new
                {
                    sentiment,
                    intensity,
                    eventData.PositiveDirections,
                    eventData.NegativeDirections,
                    eventData.RelatedProducts
                }, _jsonOptions),
                EventTime = report.PublishTime
            };

            _dbContext.EventRecord.Add(eventRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 4b. 规范化关联（关联表，供精确查询）
            await SaveEventRelationsAsync(eventRecord.Id, report.RelatedStocks,
                eventData.RelatedConcepts, cancellationToken);

            // 5. 发布到消息总线
            await _messageBus.PublishAsync("events", new
            {
                EventId = eventRecord.Id,
                EventType = "report",
                report.Title,
                sentiment.Sentiment,
                intensity.OverallScore
            });

            _logger.LogInformation("Processed report event: {Title}, Sentiment: {Sentiment}, Importance: {Importance}",
                report.Title, sentiment.Sentiment, intensity.Importance);

            return eventRecord;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process report: {Title}", report.Title);
            throw;
        }
    }

    /// <summary>
    /// 处理新闻事件
    /// </summary>
    public async Task<EventRecordEntity> ProcessNewsAsync(NewsData news, CancellationToken cancellationToken = default)
    {
        try
        {
            // 0. 去重：同 Url 已处理过则跳过（避免重复采集重复入库 + 节省 LLM 调用）
            if (!string.IsNullOrEmpty(news.Url))
            {
                var existing = await _dbContext.EventRecord
                    .FirstOrDefaultAsync(e => e.Url == news.Url, cancellationToken);
                if (existing != null)
                {
                    _logger.LogDebug("跳过重复新闻：{Title}", news.Title);
                    return existing;
                }
            }

            // 1. 抽取事件
            var eventData = await _eventExtractor.ExtractFromNewsAsync(news, cancellationToken);

            // 2+3. 情绪分析与强度评分并行（均只依赖 eventData，彼此独立）
            var sentimentTask = _sentimentAnalyzer.AnalyzeEventAsync(eventData, cancellationToken);
            var intensityTask = _intensityScorer.ScoreAsync(eventData, cancellationToken);
            await Task.WhenAll(sentimentTask, intensityTask);
            var sentiment = sentimentTask.Result;
            var intensity = intensityTask.Result;

            // 4. 保存事件记录
            var eventRecord = new EventRecordEntity
            {
                EventType = "news",
                Title = news.Title,
                Content = news.Content ?? news.Summary,
                Source = news.Source,
                Url = news.Url,
                Sentiment = sentiment.Sentiment,
                SentimentScore = sentiment.Score,
                Importance = intensity.Importance,
                Credibility = intensity.Credibility,
                RelatedStocks = string.Join(",", news.RelatedStocks),
                RelatedConcepts = string.Join(",", eventData.RelatedConcepts.Union(news.RelatedConcepts)),
                LLMAnalysis = JsonSerializer.Serialize(new
                {
                    sentiment,
                    intensity,
                    eventData.PositiveDirections,
                    eventData.NegativeDirections,
                    eventData.RelatedProducts
                }, _jsonOptions),
                EventTime = news.PublishTime
            };

            _dbContext.EventRecord.Add(eventRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 4b. 规范化关联（关联表，供精确查询）
            await SaveEventRelationsAsync(eventRecord.Id, news.RelatedStocks,
                eventData.RelatedConcepts.Union(news.RelatedConcepts), cancellationToken);

            // 5. 发布到消息总线
            await _messageBus.PublishAsync("events", new
            {
                EventId = eventRecord.Id,
                EventType = "news",
                news.Title,
                sentiment.Sentiment,
                intensity.OverallScore
            });

            _logger.LogInformation("Processed news event: {Title}, Sentiment: {Sentiment}, Importance: {Importance}",
                news.Title, sentiment.Sentiment, intensity.Importance);

            return eventRecord;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process news: {Title}", news.Title);
            throw;
        }
    }

    /// <summary>
    /// 处理政策事件
    /// </summary>
    public async Task<EventRecordEntity> ProcessPolicyAsync(string title, string content, string? source = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 抽取事件
            var eventData = await _eventExtractor.ExtractEventAsync(content, "policy", cancellationToken);
            eventData.Title = title;
            eventData.Source = source;

            // 2+3. 情绪分析与强度评分并行（均只依赖 eventData，彼此独立）
            var sentimentTask = _sentimentAnalyzer.AnalyzeEventAsync(eventData, cancellationToken);
            var intensityTask = _intensityScorer.ScoreAsync(eventData, cancellationToken);
            await Task.WhenAll(sentimentTask, intensityTask);
            var sentiment = sentimentTask.Result;
            var intensity = intensityTask.Result;

            // 4. 保存事件记录
            var eventRecord = new EventRecordEntity
            {
                EventType = "policy",
                Title = title,
                Content = content,
                Source = source,
                Sentiment = sentiment.Sentiment,
                SentimentScore = sentiment.Score,
                Importance = intensity.Importance,
                Credibility = intensity.Credibility,
                RelatedConcepts = string.Join(",", eventData.RelatedConcepts),
                LLMAnalysis = JsonSerializer.Serialize(new
                {
                    sentiment,
                    intensity,
                    eventData.PositiveDirections,
                    eventData.NegativeDirections,
                    eventData.RelatedProducts
                }, _jsonOptions),
                EventTime = DateTime.Now
            };

            _dbContext.EventRecord.Add(eventRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 4b. 规范化关联（政策无关联股票，仅概念）
            await SaveEventRelationsAsync(eventRecord.Id, Array.Empty<string>(),
                eventData.RelatedConcepts, cancellationToken);

            // 5. 发布到消息总线
            await _messageBus.PublishAsync("events", new
            {
                EventId = eventRecord.Id,
                EventType = "policy",
                Title = title,
                sentiment.Sentiment,
                intensity.OverallScore
            });

            _logger.LogInformation("Processed policy event: {Title}, Sentiment: {Sentiment}, Importance: {Importance}",
                title, sentiment.Sentiment, intensity.Importance);

            return eventRecord;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process policy: {Title}", title);
            throw;
        }
    }

    /// <summary>
    /// 获取最近事件
    /// </summary>
    public async Task<List<EventRecordEntity>> GetRecentEventsAsync(int count = 50, string? eventType = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventRecord.AsQueryable();

        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        return await query
            .OrderByDescending(e => e.EventTime)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取事件详情
    /// </summary>
    public async Task<EventRecordEntity?> GetEventAsync(long eventId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventRecord.FindAsync(new object[] { eventId }, cancellationToken);
    }

    /// <summary>
    /// 搜索事件
    /// </summary>
    /// <summary>写入事件的股票/概念关联表（去重、忽略空值）</summary>
    private async Task SaveEventRelationsAsync(long eventId, IEnumerable<string> stocks, IEnumerable<string> concepts, CancellationToken cancellationToken = default)
    {
        var added = false;

        var rawStocks = stocks.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();

        // 名称 → 代码归一：6 位代码直用 → 全名精确 → 模糊包含（命中≤上限才归一，过泛/残缺名跳过）。
        // 精确匹配走定向查询（常见、廉价）；只有存在残缺名时才加载全市场做模糊，避免每次全表加载。
        var names = rawStocks.Where(s => !_stockCodeRegex.IsMatch(s)).ToList();
        var codes = new HashSet<string>(rawStocks.Where(s => _stockCodeRegex.IsMatch(s)), StringComparer.Ordinal);

        if (names.Count > 0)
        {
            var exact = (await _dbContext.StockBase
                    .Where(s => names.Contains(s.Name))
                    .Select(s => new { s.Name, s.Code })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.First().Code);

            var unresolved = new List<string>();
            foreach (var n in names)
            {
                if (exact.TryGetValue(n, out var code)) codes.Add(code);
                else unresolved.Add(n);
            }

            // 残缺名：加载全市场做模糊包含匹配
            if (unresolved.Count > 0)
            {
                var universe = (await _dbContext.StockBase
                        .Select(s => new { s.Name, s.Code })
                        .ToListAsync(cancellationToken))
                    .Select(x => (x.Name, x.Code))
                    .ToList();
                var resolved = StockNameResolver.ResolveCodes(unresolved, universe);
                foreach (var c in resolved.Codes) codes.Add(c);
                foreach (var miss in resolved.Unresolved)
                    _logger.LogDebug("event_stock_relation 跳过无法归一为代码的股票标识：{Identifier}", miss);
            }
        }

        foreach (var code in codes)
        {
            _dbContext.EventStockRelation.Add(new EventStockRelationEntity { EventId = eventId, StockCode = code });
            added = true;
        }
        foreach (var concept in concepts.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            _dbContext.EventConceptRelation.Add(new EventConceptRelationEntity { EventId = eventId, ConceptName = concept.Trim() });
            added = true;
        }
        if (added)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 写入候选图谱边（隔离）：公司-概念归属 + 公司-公司共现。带可信度，多次提及累加。
    /// 不写入权威图谱（CompanyRelation/IndustryChain），仅作线索。
    /// </summary>
    private async Task SaveCandidateEdgesAsync(
        List<string> companies, List<string> concepts, int credibility, string? sourceUrl, CancellationToken ct)
    {
        var comps = companies.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().Take(6).ToList();
        var cons = concepts.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().Take(8).ToList();
        if (comps.Count == 0) return;

        var edges = new List<(string From, string To, string Type)>();
        // 公司 → 概念
        foreach (var comp in comps)
            foreach (var con in cons)
                edges.Add((comp, con, "concept"));
        // 公司 ↔ 公司共现（无向，按字典序定向去重）
        for (int i = 0; i < comps.Count; i++)
            for (int j = i + 1; j < comps.Count; j++)
            {
                var a = string.CompareOrdinal(comps[i], comps[j]) <= 0 ? comps[i] : comps[j];
                var b = a == comps[i] ? comps[j] : comps[i];
                edges.Add((a, b, "co-occur"));
            }

        foreach (var (from, to, type) in edges)
        {
            var existing = await _dbContext.GraphCandidateEdge
                .FirstOrDefaultAsync(e => e.FromEntity == from && e.ToEntity == to && e.EdgeType == type, ct);
            if (existing != null)
            {
                existing.MentionCount++;
                existing.Credibility = Math.Max(existing.Credibility, credibility);
                existing.LastSourceUrl = sourceUrl;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _dbContext.GraphCandidateEdge.Add(new GraphCandidateEdgeEntity
                {
                    FromEntity = from,
                    ToEntity = to,
                    EdgeType = type,
                    Credibility = credibility,
                    MentionCount = 1,
                    LastSourceUrl = sourceUrl,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }
        }
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("候选边写入：{Edges} 条（公司{Comp}×概念{Con}）", edges.Count, comps.Count, cons.Count);
    }

    /// <summary>事件是否已存在（按 Url 去重，供采集前预判，避免无谓的 LLM 调用）</summary>
    public async Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return await _dbContext.EventRecord.AnyAsync(e => e.Url == url, cancellationToken);
    }

    /// <summary>
    /// 取已入库知识星球事件的最新发布时间（增量采集水位线）。无记录返回 null。
    /// </summary>
    public async Task<DateTime?> GetLatestKnowledgeStarTimeAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventRecord
            .Where(e => e.EventType == "knowledge-star" && e.EventTime != null)
            .MaxAsync(e => (DateTime?)e.EventTime, cancellationToken);
    }

    /// <summary>
    /// 保存知识星球内容事件（已由小作文分析器分析过的结果）。
    /// </summary>
    public async Task<EventRecordEntity?> SaveKnowledgeStarEventAsync(
        KnowledgeStarContent content, EssayAnalysisResult essay, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(content.Url) && await ExistsByUrlAsync(content.Url, cancellationToken))
            return null;

        var stocks = content.RelatedStocks.Union(essay.RelatedCompanies).Distinct().ToList();
        var concepts = content.RelatedConcepts.Union(essay.RelatedConcepts).Distinct().ToList();

        var entity = new EventRecordEntity
        {
            EventType = "knowledge-star",
            Title = content.Title,
            Content = content.Content,
            Source = "知识星球",
            Url = content.Url,
            Sentiment = essay.Sentiment,
            SentimentScore = essay.SentimentScore,
            Credibility = essay.CredibilityScore,
            RelatedStocks = string.Join(",", stocks),
            RelatedConcepts = string.Join(",", concepts),
            LLMAnalysis = JsonSerializer.Serialize(new
            {
                essay.CredibilityScore,
                essay.Summary,
                essay.Conclusion,
                essay.RiskWarnings,
                content.Author
            }, _jsonOptions),
            EventTime = content.PublishTime
        };

        _dbContext.EventRecord.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SaveEventRelationsAsync(entity.Id, stocks, concepts, cancellationToken);
        // 候选边（隔离，带可信度），仅作线索
        await SaveCandidateEdgesAsync(essay.RelatedCompanies, concepts, essay.CredibilityScore, content.Url, cancellationToken);

        await _messageBus.PublishAsync("events", new
        {
            EventId = entity.Id,
            EventType = "knowledge-star",
            content.Title,
            essay.Sentiment,
            essay.CredibilityScore
        });

        _logger.LogInformation("知识星球事件入库：{Title}（可信度 {Cred}）", content.Title, essay.CredibilityScore);
        return entity;
    }

    /// <summary>按股票精确查询关联事件（用关联表 JOIN，替代逗号字符串 LIKE）</summary>
    public async Task<List<EventRecordEntity>> GetEventsByStockAsync(string stockCode, int count = 50, CancellationToken cancellationToken = default)
    {
        return await (from r in _dbContext.EventStockRelation
                      where r.StockCode == stockCode
                      join e in _dbContext.EventRecord on r.EventId equals e.Id
                      orderby e.EventTime descending
                      select e)
                     .Take(count)
                     .ToListAsync(cancellationToken);
    }

    /// <summary>按概念精确查询关联事件</summary>
    public async Task<List<EventRecordEntity>> GetEventsByConceptAsync(string concept, int count = 50, CancellationToken cancellationToken = default)
    {
        return await (from r in _dbContext.EventConceptRelation
                      where r.ConceptName == concept
                      join e in _dbContext.EventRecord on r.EventId equals e.Id
                      orderby e.EventTime descending
                      select e)
                     .Take(count)
                     .ToListAsync(cancellationToken);
    }

    public async Task<List<EventRecordEntity>> SearchEventsAsync(string keyword, int count = 50, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventRecord
            .Where(e => e.Title.Contains(keyword) || e.Content!.Contains(keyword))
            .OrderByDescending(e => e.EventTime)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取事件统计
    /// </summary>
    public async Task<EventStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventRecord.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(e => e.EventTime >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(e => e.EventTime <= endDate.Value);

        // 使用SQL聚合替代全量加载到内存
        var totalCount = await query.CountAsync(cancellationToken);
        var positiveCount = await query.CountAsync(e => e.Sentiment == "positive", cancellationToken);
        var negativeCount = await query.CountAsync(e => e.Sentiment == "negative", cancellationToken);
        var neutralCount = await query.CountAsync(e => e.Sentiment == "neutral", cancellationToken);

        var avgImportance = await query
            .Where(e => e.Importance.HasValue)
            .AverageAsync(e => (double?)e.Importance!.Value, cancellationToken) ?? 0;

        var avgCredibility = await query
            .Where(e => e.Credibility.HasValue)
            .AverageAsync(e => (double?)e.Credibility!.Value, cancellationToken) ?? 0;

        var byType = await query
            .GroupBy(e => e.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Type, g => g.Count, cancellationToken);

        return new EventStatistics
        {
            TotalCount = totalCount,
            PositiveCount = positiveCount,
            NegativeCount = negativeCount,
            NeutralCount = neutralCount,
            AverageImportance = avgImportance,
            AverageCredibility = avgCredibility,
            ByType = byType
        };
    }
}

/// <summary>
/// 事件统计
/// </summary>
public class EventStatistics
{
    public int TotalCount { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int NeutralCount { get; set; }
    public double AverageImportance { get; set; }
    public double AverageCredibility { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
}
