using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 政策分析Agent
/// </summary>
public class PolicyAnalyzerAgent : IPolicyAnalyzer
{
    private readonly ILLMService _llmService;
    private readonly ILogger<PolicyAnalyzerAgent> _logger;

    public PolicyAnalyzerAgent(ILLMService llmService, ILogger<PolicyAnalyzerAgent> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<ReportAnalysis> AnalyzeAsync(string policyTitle, string policyContent, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $"政策标题：{policyTitle}\n\n政策内容：\n{policyContent}";
            var request = new LLMRequest
            {
                SystemPrompt = @"你是一个专业的政策分析师，专注于金融和产业政策分析。请从政策中提取关键信息，包括：
1. 核心摘要（200字以内）
2. 政策要点（列表）
3. 受益产业方向（列表）
4. 受损产业方向（列表）
5. 关联概念/板块（列表）
6. 核心观点
7. 风险提示（列表）
8. 投资建议

请以JSON格式输出，格式如下：
{
  ""summary"": ""核心摘要"",
  ""exceedExpectations"": [""政策要点1"", ""政策要点2""],
  ""belowExpectations"": [""受损产业1""],
  ""industryDirections"": [""受益产业1"", ""受益产业2""],
  ""relatedConcepts"": [""概念1"", ""概念2""],
  ""coreView"": ""核心观点"",
  ""risks"": [""风险1"", ""风险2""],
  ""investmentAdvice"": ""投资建议""
}",
                UserPrompt = prompt
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (!response.Success)
            {
                _logger.LogWarning("LLM analysis failed: {Error}", response.ErrorMessage);
                return GenerateFallbackAnalysis(policyTitle, policyContent);
            }

            return ParseAnalysisResponse(response.Content, policyTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze policy: {Title}", policyTitle);
            return GenerateFallbackAnalysis(policyTitle, policyContent);
        }
    }

    private ReportAnalysis ParseAnalysisResponse(string response, string policyTitle)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            return new ReportAnalysis
            {
                Summary = root.GetProperty("summary").GetString() ?? "",
                ExceedExpectations = GetStringList(root, "exceedExpectations"),
                BelowExpectations = GetStringList(root, "belowExpectations"),
                IndustryDirections = GetStringList(root, "industryDirections"),
                RelatedConcepts = GetStringList(root, "relatedConcepts"),
                CoreView = root.TryGetProperty("coreView", out var coreView) ? coreView.GetString() : null,
                Risks = GetStringList(root, "risks"),
                InvestmentAdvice = root.TryGetProperty("investmentAdvice", out var advice) ? advice.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response, using fallback");
            return GenerateFallbackAnalysis(policyTitle, "");
        }
    }

    private static List<string> GetStringList(JsonElement root, string propertyName)
    {
        return Common.LLMResponseParser.GetStringList(root, propertyName);
    }

    private static ReportAnalysis GenerateFallbackAnalysis(string policyTitle, string policyContent)
    {
        return new ReportAnalysis
        {
            Summary = $"政策：{policyTitle}",
            CoreView = policyContent.Length > 200 ? policyContent[..200] + "..." : policyContent
        };
    }
}
