using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Intelligence.Common;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 研报分析Agent
/// </summary>
public class ReportAnalyzerAgent : IReportAnalyzer
{
    private readonly ILLMService _llmService;
    private readonly ILogger<ReportAnalyzerAgent> _logger;

    public ReportAnalyzerAgent(ILLMService llmService, ILogger<ReportAnalyzerAgent> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<ReportAnalysis> AnalyzeAsync(ReportData report, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildAnalysisPrompt(report);
            var request = new LLMRequest
            {
                SystemPrompt = @"你是一个专业的金融研报分析师。请从研报中提取关键信息，包括：
1. 核心摘要（200字以内）
2. 超预期点（列表）
3. 低于预期点（列表）
4. 产业方向（列表）
5. 关联概念/板块（列表）
6. 核心观点
7. 风险提示（列表）
8. 投资建议

请以JSON格式输出，格式如下：
{
  ""summary"": ""核心摘要"",
  ""exceedExpectations"": [""超预期点1"", ""超预期点2""],
  ""belowExpectations"": [""低于预期点1""],
  ""industryDirections"": [""产业方向1"", ""产业方向2""],
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
                return GenerateFallbackAnalysis(report);
            }

            return ParseAnalysisResponse(response.Content, report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze report: {Title}", report.Title);
            return GenerateFallbackAnalysis(report);
        }
    }

    private string BuildAnalysisPrompt(ReportData report)
    {
        var parts = new List<string>
        {
            $"研报标题：{report.Title}",
            $"来源：{report.Source}",
            $"作者/机构：{report.Author ?? "未知"}",
            $"研报类型：{report.ReportType ?? "未知"}",
            $"评级：{report.Rating ?? "未知"}",
            $"目标价：{report.TargetPrice?.ToString() ?? "无"}"
        };

        if (report.RelatedStocks.Any())
        {
            parts.Add($"关联股票：{string.Join(", ", report.RelatedStocks)}");
        }

        if (!string.IsNullOrEmpty(report.Summary))
        {
            parts.Add($"摘要：{report.Summary}");
        }

        if (!string.IsNullOrEmpty(report.Content))
        {
            var content = report.Content.Length > 3000
                ? report.Content[..3000] + "..."
                : report.Content;
            parts.Add($"内容：{content}");
        }

        return string.Join("\n", parts);
    }

    private ReportAnalysis ParseAnalysisResponse(string response, ReportData report)
    {
        try
        {
            var jsonContent = LLMResponseParser.CleanJsonResponse(response);
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            return new ReportAnalysis
            {
                Summary = root.GetProperty("summary").GetString() ?? "",
                ExceedExpectations = LLMResponseParser.GetStringList(root, "exceedExpectations"),
                BelowExpectations = LLMResponseParser.GetStringList(root, "belowExpectations"),
                IndustryDirections = LLMResponseParser.GetStringList(root, "industryDirections"),
                RelatedConcepts = LLMResponseParser.GetStringList(root, "relatedConcepts"),
                CoreView = LLMResponseParser.GetString(root, "coreView"),
                Risks = LLMResponseParser.GetStringList(root, "risks"),
                InvestmentAdvice = LLMResponseParser.GetString(root, "investmentAdvice")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response, using fallback");
            return GenerateFallbackAnalysis(report);
        }
    }

    private static List<string> GetStringList(JsonElement root, string propertyName)
    {
        return LLMResponseParser.GetStringList(root, propertyName);
    }

    private static ReportAnalysis GenerateFallbackAnalysis(ReportData report)
    {
        var analysis = new ReportAnalysis
        {
            Summary = report.Summary ?? report.Title,
            CoreView = report.Summary
        };

        if (!string.IsNullOrEmpty(report.Rating))
        {
            analysis.InvestmentAdvice = $"评级：{report.Rating}";
            if (report.TargetPrice.HasValue)
            {
                analysis.InvestmentAdvice += $"，目标价：{report.TargetPrice}";
            }
        }

        if (report.RelatedStocks.Any())
        {
            analysis.RelatedConcepts.AddRange(report.RelatedStocks);
        }

        return analysis;
    }
}
