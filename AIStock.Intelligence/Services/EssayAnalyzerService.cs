using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 小作文分析服务
/// </summary>
public class EssayAnalyzerService : IEssayAnalyzer
{
    private readonly ILLMService _llmService;
    private readonly ISentimentAnalyzer _sentimentAnalyzer;
    private readonly ICredibilityAnalyzer _credibilityAnalyzer;
    private readonly ILogger<EssayAnalyzerService> _logger;

    public EssayAnalyzerService(
        ILLMService llmService,
        ISentimentAnalyzer sentimentAnalyzer,
        ICredibilityAnalyzer credibilityAnalyzer,
        ILogger<EssayAnalyzerService> logger)
    {
        _llmService = llmService;
        _sentimentAnalyzer = sentimentAnalyzer;
        _credibilityAnalyzer = credibilityAnalyzer;
        _logger = logger;
    }

    public async Task<EssayAnalysisResult> AnalyzeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = new EssayAnalysisResult
        {
            OriginalContent = text,
            ParsedContent = text,
            ContentType = "text"
        };

        try
        {
            // 1. 提取关联公司和概念
            var extractionResult = await ExtractEntitiesAsync(text, cancellationToken);
            result.RelatedCompanies = extractionResult.companies;
            result.RelatedConcepts = extractionResult.concepts;

            // 2. 情绪分析
            var sentiment = await _sentimentAnalyzer.AnalyzeAsync(text, cancellationToken);
            result.Sentiment = sentiment.Sentiment;
            result.SentimentScore = sentiment.Score;

            // 3. 可信度分析
            var eventData = new EventData
            {
                EventType = "essay",
                Title = text.Length > 100 ? text[..100] + "..." : text,
                Content = text,
                RelatedCompanies = result.RelatedCompanies.ToDictionary(c => c, c => ""),
                RelatedConcepts = result.RelatedConcepts
            };

            var credibility = await _credibilityAnalyzer.AnalyzeAsync(eventData, cancellationToken);
            result.CredibilityScore = credibility.CredibilityScore;
            result.RiskWarnings = credibility.RiskWarnings;

            // 4. 生成摘要
            result.Summary = await GenerateSummaryAsync(text, cancellationToken);

            // 5. 生成结论
            result.Conclusion = GenerateConclusion(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze essay");
            result.Conclusion = "分析失败，请稍后重试";
        }

        return result;
    }

    public async Task<EssayAnalysisResult> AnalyzeImageAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        // OCR 功能需要接入第三方服务（如百度OCR、腾讯OCR等）
        // 这里返回一个模拟结果
        var result = new EssayAnalysisResult
        {
            OriginalContent = imageUrl,
            ContentType = "image",
            ParsedContent = "[OCR功能需要接入第三方服务]",
            CredibilityScore = 50,
            Conclusion = "图片分析功能需要接入OCR服务"
        };

        return result;
    }

    public async Task<EssayAnalysisResult> AnalyzeAudioAsync(string audioUrl, CancellationToken cancellationToken = default)
    {
        // ASR 功能需要接入第三方服务（如百度ASR、腾讯ASR等）
        // 这里返回一个模拟结果
        var result = new EssayAnalysisResult
        {
            OriginalContent = audioUrl,
            ContentType = "audio",
            ParsedContent = "[ASR功能需要接入第三方服务]",
            CredibilityScore = 50,
            Conclusion = "音频分析功能需要接入ASR服务"
        };

        return result;
    }

    private async Task<(List<string> companies, List<string> concepts)> ExtractEntitiesAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $@"请从以下文本中提取关联的公司和概念：

文本：{text}

请以JSON格式返回：
{{""companies"": [""公司1"", ""公司2""], ""concepts"": [""概念1"", ""概念2""]}}";

            var request = new LLMRequest
            {
                SystemPrompt = "你是一个专业的金融信息分析师，请从文本中提取关联的公司和概念。",
                UserPrompt = prompt
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (response.Success)
            {
                var jsonContent = CleanJsonResponse(response.Content);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;

                var companies = new List<string>();
                if (root.TryGetProperty("companies", out var companiesArray))
                {
                    foreach (var item in companiesArray.EnumerateArray())
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrEmpty(value))
                            companies.Add(value);
                    }
                }

                var concepts = new List<string>();
                if (root.TryGetProperty("concepts", out var conceptsArray))
                {
                    foreach (var item in conceptsArray.EnumerateArray())
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrEmpty(value))
                            concepts.Add(value);
                    }
                }

                return (companies, concepts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract entities");
        }

        return (new List<string>(), new List<string>());
    }

    private async Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            if (text.Length <= 200)
                return text;

            var prompt = $@"请为以下文本生成一个简短的摘要（100字以内）：

文本：{text}";

            var request = new LLMRequest
            {
                SystemPrompt = "你是一个专业的文本分析师，请生成简洁的摘要。",
                UserPrompt = prompt
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (response.Success)
            {
                return response.Content;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate summary");
        }

        return text.Length > 200 ? text[..200] + "..." : text;
    }

    private string GenerateConclusion(EssayAnalysisResult result)
    {
        var conclusions = new List<string>();

        if (result.CredibilityScore >= 70)
        {
            conclusions.Add("内容可信度较高");
        }
        else if (result.CredibilityScore <= 30)
        {
            conclusions.Add("内容可信度较低，需谨慎对待");
        }
        else
        {
            conclusions.Add("内容可信度一般");
        }

        if (result.Sentiment == "positive")
        {
            conclusions.Add("情绪倾向积极");
        }
        else if (result.Sentiment == "negative")
        {
            conclusions.Add("情绪倾向消极");
        }

        if (result.RelatedCompanies.Any())
        {
            conclusions.Add($"关联公司：{string.Join("、", result.RelatedCompanies.Take(3))}");
        }

        if (result.RiskWarnings.Any())
        {
            conclusions.Add("存在风险提示");
        }

        return string.Join("，", conclusions);
    }

    private string CleanJsonResponse(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```json")) json = json.Substring(7);
        if (json.StartsWith("```")) json = json.Substring(3);
        if (json.EndsWith("```")) json = json.Substring(0, json.Length - 3);
        return json.Trim();
    }
}
