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
            // 1. 抽取事件
            var eventData = await _eventExtractor.ExtractFromReportAsync(report, cancellationToken);

            // 2. 情绪分析
            var sentiment = await _sentimentAnalyzer.AnalyzeEventAsync(eventData, cancellationToken);

            // 3. 强度评分
            var intensity = await _intensityScorer.ScoreAsync(eventData, cancellationToken);

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
                RelatedStocks = string.Join(",", report.RelatedStocks),
                RelatedConcepts = string.Join(",", eventData.RelatedConcepts),
                LLMAnalysis = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sentiment,
                    intensity,
                    eventData.PositiveDirections,
                    eventData.NegativeDirections,
                    eventData.RelatedProducts
                }),
                EventTime = report.PublishTime
            };

            _dbContext.EventRecord.Add(eventRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

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
            // 1. 抽取事件
            var eventData = await _eventExtractor.ExtractFromNewsAsync(news, cancellationToken);

            // 2. 情绪分析
            var sentiment = await _sentimentAnalyzer.AnalyzeEventAsync(eventData, cancellationToken);

            // 3. 强度评分
            var intensity = await _intensityScorer.ScoreAsync(eventData, cancellationToken);

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
                LLMAnalysis = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sentiment,
                    intensity,
                    eventData.PositiveDirections,
                    eventData.NegativeDirections,
                    eventData.RelatedProducts
                }),
                EventTime = news.PublishTime
            };

            _dbContext.EventRecord.Add(eventRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

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

            // 2. 情绪分析
            var sentiment = await _sentimentAnalyzer.AnalyzeEventAsync(eventData, cancellationToken);

            // 3. 强度评分
            var intensity = await _intensityScorer.ScoreAsync(eventData, cancellationToken);

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
                LLMAnalysis = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sentiment,
                    intensity,
                    eventData.PositiveDirections,
                    eventData.NegativeDirections,
                    eventData.RelatedProducts
                }),
                EventTime = DateTime.UtcNow
            };

            _dbContext.EventRecord.Add(eventRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

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
