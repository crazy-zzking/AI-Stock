using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 情绪分析服务
/// </summary>
public class SentimentAnalysisService : ISentimentAnalyzer
{
    private readonly ILLMService _llmService;
    private readonly ILogger<SentimentAnalysisService> _logger;

    public SentimentAnalysisService(ILLMService llmService, ILogger<SentimentAnalysisService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<SentimentResult> AnalyzeAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new LLMRequest
            {
                SystemPrompt = @"你是一个专业的金融情绪分析专家。请分析文本的情绪倾向，包括：
1. 情绪类型：positive（利好）、negative（利空）、neutral（中性）
2. 情绪分数：-1（极度利空）到 1（极度利好）
3. 置信度：0 到 1
4. 关键词：影响情绪判断的关键词
5. 分析理由

请以JSON格式输出，格式如下：
{
  ""sentiment"": ""positive/negative/neutral"",
  ""score"": 0.5,
  ""confidence"": 0.8,
  ""keywords"": [""关键词1"", ""关键词2""],
  ""reason"": ""分析理由""
}",
                UserPrompt = $"请分析以下文本的情绪倾向：\n\n{text}"
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (!response.Success)
            {
                _logger.LogWarning("LLM sentiment analysis failed: {Error}", response.ErrorMessage);
                return GenerateFallbackSentiment(text);
            }

            return ParseSentimentResponse(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze sentiment");
            return GenerateFallbackSentiment(text);
        }
    }

    public async Task<SentimentResult> AnalyzeEventAsync(EventData eventData, CancellationToken cancellationToken = default)
    {
        var text = $"事件标题：{eventData.Title}\n";
        if (!string.IsNullOrEmpty(eventData.Content))
            text += $"内容：{eventData.Content}\n";
        if (eventData.PositiveDirections.Any())
            text += $"利好方向：{string.Join(", ", eventData.PositiveDirections)}\n";
        if (eventData.NegativeDirections.Any())
            text += $"利空方向：{string.Join(", ", eventData.NegativeDirections)}\n";

        return await AnalyzeAsync(text, cancellationToken);
    }

    private SentimentResult ParseSentimentResponse(string response)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            var result = new SentimentResult
            {
                Sentiment = root.GetProperty("sentiment").GetString() ?? "neutral",
                Score = root.GetProperty("score").GetDecimal(),
                Confidence = root.GetProperty("confidence").GetDecimal(),
                Reason = root.TryGetProperty("reason", out var reason) ? reason.GetString() : null
            };

            if (root.TryGetProperty("keywords", out var keywords))
            {
                foreach (var item in keywords.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                        result.Keywords.Add(value);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse sentiment response");
            return new SentimentResult { Sentiment = "neutral" };
        }
    }

    private static SentimentResult GenerateFallbackSentiment(string text)
    {
        var positiveWords = new[] { "利好", "上涨", "增长", "突破", "创新高", "业绩大增", "超预期", "买入", "增持" };
        var negativeWords = new[] { "利空", "下跌", "下降", "跌破", "创新低", "业绩下滑", "低于预期", "卖出", "减持" };

        var positiveCount = positiveWords.Count(w => text.Contains(w));
        var negativeCount = negativeWords.Count(w => text.Contains(w));

        var result = new SentimentResult();

        if (positiveCount > negativeCount)
        {
            result.Sentiment = "positive";
            result.Score = Math.Min(1.0m, positiveCount * 0.2m);
            result.Confidence = 0.5m;
        }
        else if (negativeCount > positiveCount)
        {
            result.Sentiment = "negative";
            result.Score = Math.Max(-1.0m, -negativeCount * 0.2m);
            result.Confidence = 0.5m;
        }
        else
        {
            result.Sentiment = "neutral";
            result.Score = 0;
            result.Confidence = 0.3m;
        }

        return result;
    }
}
