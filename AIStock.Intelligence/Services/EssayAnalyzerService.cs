using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 小作文分析服务
/// </summary>
public class EssayAnalyzerService : IEssayAnalyzer
{
    private readonly ILLMService _llmService;
    private readonly ISentimentAnalyzer _sentimentAnalyzer;
    private readonly ICredibilityAnalyzer _credibilityAnalyzer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<EssayAnalyzerService> _logger;

    private const string NameCodeCacheKey = "aistock:cache:stock_name_code";

    public EssayAnalyzerService(
        ILLMService llmService,
        ISentimentAnalyzer sentimentAnalyzer,
        ICredibilityAnalyzer credibilityAnalyzer,
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        ILogger<EssayAnalyzerService> logger)
    {
        _llmService = llmService;
        _sentimentAnalyzer = sentimentAnalyzer;
        _credibilityAnalyzer = credibilityAnalyzer;
        _scopeFactory = scopeFactory;
        _redis = redis;
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

        // 记录输入数据源
        result.AddDataSource("input", "文本内容", $"文本长度：{text.Length}", 1);

        try
        {
            // 1. 提取关联公司和概念
            var extractionResult = await ExtractEntitiesAsync(text, cancellationToken);
            result.RelatedCompanies = extractionResult.companies;
            result.RelatedConcepts = extractionResult.concepts;
            result.AddDataSource("llm", "LLM实体提取", $"提取{extractionResult.companies.Count}个公司，{extractionResult.concepts.Count}个概念", 1);

            // 2. 情绪分析
            var sentiment = await _sentimentAnalyzer.AnalyzeAsync(text, cancellationToken);
            result.Sentiment = sentiment.Sentiment;
            result.SentimentScore = sentiment.Score;
            result.AddDataSource("llm", "LLM情绪分析", $"情绪：{sentiment.Sentiment}，分数：{sentiment.Score}", 1);

            // 3. 可信度分析
            var eventData = new EventData
            {
                EventType = "essay",
                Title = text.Length > 100 ? text[..100] + "..." : text,
                Content = text,
                RelatedCompanies = await MatchCompanyCodesAsync(result.RelatedCompanies),
                RelatedConcepts = result.RelatedConcepts
            };

            var credibility = await _credibilityAnalyzer.AnalyzeAsync(eventData, cancellationToken);
            result.CredibilityScore = credibility.CredibilityScore;
            result.RiskWarnings = credibility.RiskWarnings;
            result.DataSources.AddRange(credibility.DataSources);

            // 4. 生成摘要
            result.Summary = await GenerateSummaryAsync(text, cancellationToken);
            result.AddDataSource("llm", "LLM摘要生成", "生成文本摘要", 1);

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

    public Task<EssayAnalysisResult> AnalyzeImageAsync(string imageUrl, CancellationToken cancellationToken = default)
        => AnalyzeImagesAsync(new[] { imageUrl }, null, cancellationToken);

    public async Task<EssayAnalysisResult> AnalyzeImagesAsync(IReadOnlyList<string> imageUrls, string? accompanyingText = null, CancellationToken cancellationToken = default)
    {
        var urls = (imageUrls ?? Array.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var originalContent = string.Join(",", urls);

        if (urls.Count == 0)
        {
            // 无图片时退化为纯文本分析
            return string.IsNullOrWhiteSpace(accompanyingText)
                ? EmptyImageResult(originalContent, "无可分析的图片内容")
                : await AnalyzeTextAsync(accompanyingText!, cancellationToken);
        }

        // 选一个支持多模态且优先级最高的可用模型
        var models = await _llmService.GetAvailableModelsAsync();
        var mmModel = models.Where(m => m.SupportsMultimodal).OrderBy(m => m.Priority).FirstOrDefault();
        if (mmModel == null)
        {
            _logger.LogWarning("未配置支持多模态的 LLM 模型，跳过图片识别（{Count} 张）", urls.Count);
            return EmptyImageResult(originalContent, "未配置支持多模态的 LLM 模型");
        }

        // 1. 多模态模型识别图片 → 文本（OCR + 描述）
        string recognizedText;
        try
        {
            var request = new LLMRequest
            {
                ModelId = mmModel.Id,
                SystemPrompt = "你是一个图片信息识别助手。请准确识别图片中的所有文字，并简要描述与A股/股票相关的关键信息（如个股、概念、研报观点、走势图要点等）。只输出识别到的内容本身，不要添加解释或免责声明。",
                UserPrompt = string.IsNullOrWhiteSpace(accompanyingText)
                    ? "请识别以下图片中的全部文字与股票相关信息。"
                    : $"配文：{accompanyingText}\n\n请结合配文，识别以下图片中的全部文字与股票相关信息。",
                ImageUrls = urls
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("多模态识别失败 model={Model}: {Error}", mmModel.Id, response.ErrorMessage);
                return EmptyImageResult(originalContent, "图片识别失败：" + (response.ErrorMessage ?? "无返回内容"));
            }
            recognizedText = response.Content.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "多模态识别异常 model={Model}", mmModel.Id);
            return EmptyImageResult(originalContent, "图片识别异常：" + ex.Message);
        }

        // 2. 把识别文本（含配文）复用文本分析管线，得到实体/情绪/可信度/摘要
        var combinedText = string.IsNullOrWhiteSpace(accompanyingText)
            ? recognizedText
            : $"{accompanyingText}\n{recognizedText}";

        var result = await AnalyzeTextAsync(combinedText, cancellationToken);
        result.ContentType = "image";
        result.OriginalContent = originalContent;
        result.ParsedContent = recognizedText;
        result.AddDataSource("llm", "多模态图片识别", $"识别 {urls.Count} 张图片（模型 {mmModel.Name}）", 1);
        return result;
    }

    private static EssayAnalysisResult EmptyImageResult(string originalContent, string conclusion) => new()
    {
        OriginalContent = originalContent,
        ContentType = "image",
        ParsedContent = string.Empty,
        CredibilityScore = 50,
        Conclusion = conclusion
    };

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
            var prompt = $@"请从以下文本中提取关联A股的公司和概念：

文本：{text}

请以JSON格式返回：
{{""companies"": [""公司1"", ""公司2""], ""concepts"": [""概念1"", ""概念2""]}}";

            var request = new LLMRequest
            {
                SystemPrompt = @"请根据输入文本，提取其中涉及的A股上市公司以及对应受益的概念标签。
要求：
1.仅提取A股上市公司，不要输出海外公司、未上市公司或私募企业。
2.每家公司对应一个或多个概念。
3.概念必须直接来源于文本内容，不要过度联想。
4.如果文本提到产业方向但未明确对应A股公司，则不要输出。
5.返回标准JSON格式，不要添加任何解释、Markdown或额外文字",
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

    private static string CleanJsonResponse(string response)
    {
        return Common.LLMResponseParser.CleanJsonResponse(response);
    }

    /// <summary>
    /// 根据公司名称从 stock_base 表匹配股票代码（Redis 缓存）
    /// </summary>
    /// <returns>Dictionary: 公司名称 → 股票代码</returns>
    private async Task<Dictionary<string, string>> MatchCompanyCodesAsync(List<string> companyNames)
    {
        var result = new Dictionary<string, string>();
        if (companyNames.Count == 0)
            return result;

        try
        {
            var nameToCode = await GetNameToCodeMapAsync();

            foreach (var name in companyNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var trimmed = name.Trim();

                // 1. 精确匹配
                if (nameToCode.TryGetValue(trimmed, out var code))
                {
                    result[trimmed] = code;
                    continue;
                }

                // 2. LLM 可能返回带括号后缀（如"贵州茅台(600519)"）
                var parenIdx = trimmed.IndexOf('(');
                if (parenIdx > 0 && nameToCode.TryGetValue(trimmed[..parenIdx].Trim(), out code))
                {
                    result[trimmed[..parenIdx].Trim()] = code;
                    continue;
                }

                // 3. 模糊匹配
                var matched = nameToCode
                    .Where(kv => kv.Key.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                              || trimmed.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matched.Count == 1)
                {
                    result[matched[0].Key] = matched[0].Value;
                }
                else if (matched.Count > 1)
                {
                    var best = matched.OrderBy(kv => kv.Key.Length).First();
                    result[best.Key] = best.Value;
                }
                else
                {
                    result[trimmed] = string.Empty;
                    _logger.LogDebug("Company name '{Name}' not found in stock_base", trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to match company names to stock codes");
            foreach (var name in companyNames)
            {
                if (!string.IsNullOrWhiteSpace(name) && !result.ContainsKey(name.Trim()))
                    result[name.Trim()] = string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// 获取名称→代码映射（Redis 缓存 1 小时）
    /// </summary>
    private async Task<Dictionary<string, string>> GetNameToCodeMapAsync()
    {
        var db = _redis.GetDatabase();

        // 1. 从 Redis 读取
        var cached = await db.StringGetAsync(NameCodeCacheKey);
        if (!cached.IsNullOrEmpty)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(cached!)
                       ?? await LoadFromDbAndCacheAsync();
            }
            catch
            {
                return await LoadFromDbAndCacheAsync();
            }
        }

        // 2. 缓存未命中
        return await LoadFromDbAndCacheAsync();
    }

    private async Task<Dictionary<string, string>> LoadFromDbAndCacheAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var allStocks = await dbContext.StockBase
            .Where(s => !s.IsDelisted)
            .Select(s => new { s.Code, s.Name })
            .ToListAsync();

        var nameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in allStocks)
        {
            if (!string.IsNullOrEmpty(s.Name) && !nameToCode.ContainsKey(s.Name))
                nameToCode[s.Name] = s.Code;
        }

        // 写入 Redis，1 小时过期
        var redisDb = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(nameToCode);
        await redisDb.StringSetAsync(NameCodeCacheKey, json, TimeSpan.FromHours(1));
        _logger.LogDebug("Stock name→code map cached to Redis ({Count} stocks)", nameToCode.Count);

        return nameToCode;
    }
}
