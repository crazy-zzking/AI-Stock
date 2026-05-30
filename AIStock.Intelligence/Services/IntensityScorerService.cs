using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 强度评分服务
/// </summary>
public class IntensityScorerService : IIntensityScorer
{
    private readonly ILLMService _llmService;
    private readonly ILogger<IntensityScorerService> _logger;

    public IntensityScorerService(ILLMService llmService, ILogger<IntensityScorerService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<IntensityScore> ScoreAsync(EventData eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildScoringPrompt(eventData);
            var request = new LLMRequest
            {
                SystemPrompt = @"你是一个专业的金融事件评估专家。请评估事件的强度，包括：
1. 重磅程度（1-10）：事件对市场的影响程度
2. 可信度（1-10）：事件信息的可信程度
3. 传播速度（1-10）：事件传播的速度和范围
4. 综合评分（1-10）：综合以上因素的评分
5. 评分理由

请以JSON格式输出，格式如下：
{
  ""importance"": 8,
  ""credibility"": 7,
  ""spreadSpeed"": 6,
  ""overallScore"": 7,
  ""reason"": ""评分理由"",
  ""keyFactors"": [""关键因素1"", ""关键因素2""]
}",
                UserPrompt = prompt
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (!response.Success)
            {
                _logger.LogWarning("LLM scoring failed: {Error}", response.ErrorMessage);
                return GenerateFallbackScore(eventData);
            }

            return ParseScoreResponse(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to score event intensity");
            return GenerateFallbackScore(eventData);
        }
    }

    private string BuildScoringPrompt(EventData eventData)
    {
        var parts = new List<string>
        {
            $"事件类型：{eventData.EventType}",
            $"事件标题：{eventData.Title}"
        };

        if (!string.IsNullOrEmpty(eventData.Content))
        {
            var content = eventData.Content.Length > 1000
                ? eventData.Content[..1000] + "..."
                : eventData.Content;
            parts.Add($"事件内容：{content}");
        }

        if (eventData.RelatedCompanies.Any())
        {
            parts.Add($"关联公司：{string.Join(", ", eventData.RelatedCompanies.Keys)}");
        }

        if (eventData.PositiveDirections.Any())
        {
            parts.Add($"利好方向：{string.Join(", ", eventData.PositiveDirections)}");
        }

        if (eventData.NegativeDirections.Any())
        {
            parts.Add($"利空方向：{string.Join(", ", eventData.NegativeDirections)}");
        }

        if (eventData.RelatedConcepts.Any())
        {
            parts.Add($"关联概念：{string.Join(", ", eventData.RelatedConcepts)}");
        }

        return string.Join("\n", parts);
    }

    private IntensityScore ParseScoreResponse(string response)
    {
        try
        {
            var jsonContent = Common.LLMResponseParser.CleanJsonResponse(response);
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            return new IntensityScore
            {
                Importance = Common.LLMResponseParser.GetInt(root, "importance", 5),
                Credibility = Common.LLMResponseParser.GetInt(root, "credibility", 5),
                SpreadSpeed = Common.LLMResponseParser.GetInt(root, "spreadSpeed", 5),
                OverallScore = Common.LLMResponseParser.GetInt(root, "overallScore", 5),
                Reason = Common.LLMResponseParser.GetString(root, "reason")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse score response");
            return new IntensityScore { OverallScore = 5 };
        }
    }

    private static IntensityScore GenerateFallbackScore(EventData eventData)
    {
        var score = new IntensityScore
        {
            Importance = 5,
            Credibility = 5,
            SpreadSpeed = 5,
            OverallScore = 5
        };

        // 根据事件类型调整分数
        switch (eventData.EventType)
        {
            case "report":
                score.Credibility = 7;
                score.Importance = 6;
                break;
            case "news":
                score.SpreadSpeed = 7;
                break;
            case "policy":
                score.Importance = 8;
                score.Credibility = 8;
                break;
        }

        // 根据关联公司数量调整
        if (eventData.RelatedCompanies.Count > 3)
        {
            score.Importance = Math.Min(10, score.Importance + 2);
        }

        // 根据利好/利空方向调整
        if (eventData.PositiveDirections.Any() || eventData.NegativeDirections.Any())
        {
            score.OverallScore = Math.Min(10, score.OverallScore + 1);
        }

        return score;
    }
}
