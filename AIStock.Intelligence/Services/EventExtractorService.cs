using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Intelligence.Common;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 事件抽取引擎
/// </summary>
public class EventExtractorService : IEventExtractor
{
    private readonly ILLMService _llmService;
    private readonly ILogger<EventExtractorService> _logger;

    public EventExtractorService(ILLMService llmService, ILogger<EventExtractorService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<EventData> ExtractEventAsync(string text, string eventType, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new LLMRequest
            {
                SystemPrompt = @"你是一个专业的金融事件分析专家。请从文本中提取事件信息，包括：
1. 关联公司（公司名称 -> 股票代码，如果知道的话）
2. 关联产品/服务
3. 利好方向（列表）
4. 利空方向（列表）
5. 关联概念/板块（列表）

请以JSON格式输出，格式如下：
{
  ""relatedCompanies"": {""公司名称"": ""股票代码""},
  ""relatedProducts"": [""产品1"", ""产品2""],
  ""positiveDirections"": [""利好方向1""],
  ""negativeDirections"": [""利空方向1""],
  ""relatedConcepts"": [""概念1"", ""概念2""]
}",
                UserPrompt = $"事件类型：{eventType}\n\n文本内容：\n{text}"
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (!response.Success)
            {
                _logger.LogWarning("LLM extraction failed: {Error}", response.ErrorMessage);
                return GenerateFallbackEvent(text, eventType);
            }

            return ParseEventResponse(response.Content, text, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract event");
            return GenerateFallbackEvent(text, eventType);
        }
    }

    public async Task<EventData> ExtractFromReportAsync(ReportData report, CancellationToken cancellationToken = default)
    {
        var text = $"研报标题：{report.Title}\n来源：{report.Source}\n";
        if (!string.IsNullOrEmpty(report.Summary))
            text += $"摘要：{report.Summary}\n";
        if (!string.IsNullOrEmpty(report.Content))
            text += $"内容：{report.Content[..Math.Min(2000, report.Content.Length)]}\n";

        var eventData = await ExtractEventAsync(text, "report", cancellationToken);
        eventData.Title = report.Title;
        eventData.Source = report.Source;
        eventData.Url = report.Url;
        eventData.EventTime = report.PublishTime;

        foreach (var stock in report.RelatedStocks)
        {
            if (!eventData.RelatedCompanies.ContainsKey(stock))
                eventData.RelatedCompanies[stock] = stock;
        }

        return eventData;
    }

    public async Task<EventData> ExtractFromNewsAsync(NewsData news, CancellationToken cancellationToken = default)
    {
        var text = $"新闻标题：{news.Title}\n来源：{news.Source}\n";
        if (!string.IsNullOrEmpty(news.Summary))
            text += $"摘要：{news.Summary}\n";
        if (!string.IsNullOrEmpty(news.Content))
            text += $"内容：{news.Content[..Math.Min(2000, news.Content.Length)]}\n";

        var eventData = await ExtractEventAsync(text, "news", cancellationToken);
        eventData.Title = news.Title;
        eventData.Source = news.Source;
        eventData.Url = news.Url;
        eventData.EventTime = news.PublishTime;
        eventData.RelatedConcepts.AddRange(news.RelatedConcepts);

        foreach (var stock in news.RelatedStocks)
        {
            if (!eventData.RelatedCompanies.ContainsKey(stock))
                eventData.RelatedCompanies[stock] = stock;
        }

        return eventData;
    }

    private EventData ParseEventResponse(string response, string text, string eventType)
    {
        try
        {
            var jsonContent = LLMResponseParser.CleanJsonResponse(response);
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            var eventData = new EventData
            {
                EventType = eventType,
                Title = ExtractTitle(text),
                Content = text,
                EventTime = DateTime.Now
            };

            if (root.TryGetProperty("relatedCompanies", out var companies))
            {
                foreach (var prop in companies.EnumerateObject())
                {
                    eventData.RelatedCompanies[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            if (root.TryGetProperty("relatedProducts", out var products))
            {
                foreach (var item in products.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                        eventData.RelatedProducts.Add(value);
                }
            }

            if (root.TryGetProperty("positiveDirections", out var positive))
            {
                foreach (var item in positive.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                        eventData.PositiveDirections.Add(value);
                }
            }

            if (root.TryGetProperty("negativeDirections", out var negative))
            {
                foreach (var item in negative.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                        eventData.NegativeDirections.Add(value);
                }
            }

            if (root.TryGetProperty("relatedConcepts", out var concepts))
            {
                foreach (var item in concepts.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                        eventData.RelatedConcepts.Add(value);
                }
            }

            return eventData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse event response, using fallback");
            return GenerateFallbackEvent(text, eventType);
        }
    }

    private static string ExtractTitle(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("研报标题：") || line.StartsWith("新闻标题："))
                return line.Substring(line.IndexOf('：') + 1).Trim();
        }
        return lines.Length > 0 ? lines[0][..Math.Min(100, lines[0].Length)] : "未知事件";
    }

    private static EventData GenerateFallbackEvent(string text, string eventType)
    {
        return new EventData
        {
            EventType = eventType,
            Title = ExtractTitle(text),
            Content = text,
            EventTime = DateTime.Now
        };
    }
}
