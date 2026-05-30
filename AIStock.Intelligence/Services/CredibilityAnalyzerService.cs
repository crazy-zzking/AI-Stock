using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 真假识别服务
/// </summary>
public class CredibilityAnalyzerService : ICredibilityAnalyzer
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILLMService _llmService;
    private readonly ILogger<CredibilityAnalyzerService> _logger;

    public CredibilityAnalyzerService(
        AIStockDbContext dbContext,
        ILLMService llmService,
        ILogger<CredibilityAnalyzerService> logger)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<CredibilityResult> AnalyzeAsync(EventData eventData, CancellationToken cancellationToken = default)
    {
        var result = new CredibilityResult();

        // 记录输入数据源
        result.AddDataSource("input", "事件数据", $"标题：{eventData.Title}", 1);

        // 1. 检查历史重复
        var duplicates = await CheckHistoricalDuplicatesAsync(eventData.Title, eventData.Content ?? "", cancellationToken);
        result.HasHistoricalDuplicate = duplicates.Any();
        result.DuplicateEvents = duplicates;
        result.AddDataSource("database", "事件记录表", $"查询近1000条事件，找到{duplicates.Count}条相似", duplicates.Count);

        // 2. 逻辑闭环分析
        var logicAnalysis = await AnalyzeLogicAsync(eventData, cancellationToken);
        result.LogicScore = logicAnalysis.score;
        result.LogicAnalysis = logicAnalysis.analysis;
        result.AddDataSource("llm", "LLM逻辑分析", "使用LLM分析事件逻辑合理性", 1);

        // 3. 资金配合分析（简化版，实际需要接入行情数据）
        var capitalAnalysis = await AnalyzeCapitalAsync(eventData, cancellationToken);
        result.CapitalScore = capitalAnalysis.score;
        result.CapitalAnalysis = capitalAnalysis.analysis;
        result.AddDataSource("database", "K线数据", $"分析{eventData.RelatedCompanies.Count}只关联股票的资金流动", eventData.RelatedCompanies.Count);

        // 4. 计算综合可信度
        result.CredibilityScore = CalculateCredibilityScore(result);

        // 5. 综合判断
        result.Verdict = DetermineVerdict(result);
        result.Reason = GenerateReason(result);

        // 6. 风险提示
        result.RiskWarnings = GenerateRiskWarnings(result);

        return result;
    }

    public async Task<List<string>> CheckHistoricalDuplicatesAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var duplicates = new List<string>();

        // 从事件记录中查找相似事件
        var recentEvents = await _dbContext.EventRecord
            .Where(e => e.EventType == "news" || e.EventType == "report")
            .OrderByDescending(e => e.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        foreach (var evt in recentEvents)
        {
            var similarity = CalculateSimilarity(title, evt.Title);
            if (similarity > 0.8) // 相似度超过80%
            {
                duplicates.Add($"[{evt.CreatedAt:yyyy-MM-dd}] {evt.Title}");
            }
        }

        return duplicates.Take(5).ToList();
    }

    private async Task<(int score, string analysis)> AnalyzeLogicAsync(EventData eventData, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $@"请分析以下事件的逻辑合理性：

标题：{eventData.Title}
内容：{eventData.Content ?? "无"}

请从以下角度分析：
1. 事件是否符合常理
2. 数据是否合理
3. 是否存在逻辑漏洞
4. 是否有夸大或误导成分

请返回JSON格式：
{{""score"": 0-100, ""analysis"": ""分析结论""}}";

            var request = new LLMRequest
            {
                SystemPrompt = "你是一个专业的信息验证分析师，请客观分析事件的逻辑合理性。",
                UserPrompt = prompt
            };

            var response = await _llmService.SendAsync(request, cancellationToken);
            if (response.Success)
            {
                var jsonContent = CleanJsonResponse(response.Content);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;

                var score = root.GetProperty("score").GetInt32();
                var analysis = root.GetProperty("analysis").GetString() ?? "";

                return (score, analysis);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze logic");
        }

        return (50, "逻辑分析失败");
    }

    private async Task<(int score, string analysis)> AnalyzeCapitalAsync(EventData eventData, CancellationToken cancellationToken)
    {
        // 关联公司字典为「公司名 -> 股票代码」，资金分析需用代码（Values）
        var codes = eventData.RelatedCompanies.Values
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        if (codes.Count == 0)
        {
            return (50, "无关联股票代码，无法分析资金配合");
        }

        const string interval = "Daily"; // 与落库 Interval 一致（大小写敏感）
        var eventDate = eventData.EventTime == default ? DateTime.Now.Date : eventData.EventTime.Date;

        const int preWindowDays = 5;   // 发布前窗口（主力提前建仓）
        const double preThreshold = 1.8;
        const double postThreshold = 2.0;

        var preSignals = new List<string>();   // 提前放量（更可疑）
        var postSignals = new List<string>();  // 当日放量
        var analyzed = 0;

        foreach (var code in codes.Take(5))
        {
            // 取消息日期前后一段日K（发布日后几天 + 之前约30日）
            var klines = await _dbContext.KlineData
                .Where(k => k.Code == code && k.Interval == interval && k.DateTime <= eventDate.AddDays(5))
                .OrderByDescending(k => k.DateTime)
                .Take(40)
                .ToListAsync(cancellationToken);

            if (klines.Count < 10) continue; // 数据不足
            analyzed++;

            // 发布日当天/之后首个交易日
            var post = klines.Where(k => k.DateTime.Date >= eventDate).OrderBy(k => k.DateTime).FirstOrDefault()
                       ?? klines.First();
            // 发布前窗口（紧邻发布日的前几个交易日）— 主力提前进场
            var preWindow = klines.Where(k => k.DateTime < post.DateTime).Take(preWindowDays).ToList();
            // 基线（更早的约20个交易日）
            var baseline = klines.Where(k => k.DateTime < post.DateTime).Skip(preWindowDays).Take(20).ToList();
            if (baseline.Count < 5 || preWindow.Count < 2) continue;

            var baseAvg = baseline.Average(k => (double)k.Volume);
            if (baseAvg <= 0) continue;

            var preAvg = preWindow.Average(k => (double)k.Volume);
            if (preAvg > baseAvg * preThreshold)
                preSignals.Add($"{code}(发布前{preWindowDays}日均量放大{preAvg / baseAvg:F1}倍)");
            else if (post.Volume > baseAvg * postThreshold)
                postSignals.Add($"{code}(发布当日放量{post.Volume / baseAvg:F1}倍)");
        }

        if (analyzed == 0)
            return (50, "关联股票无足够K线数据，无法验证资金配合");

        // 提前放量最可疑（主力先知先行），其次当日放量
        if (preSignals.Count > 0)
            return (80, $"消息发布前已现异常放量（疑主力提前进场）：{string.Join("、", preSignals)}");
        if (postSignals.Count > 0)
            return (72, $"消息发布当日放量：{string.Join("、", postSignals)}，需警惕资金配合");

        return (60, $"已核{analyzed}只关联股票，发布前后未见明显资金异动");
    }

    private int CalculateCredibilityScore(CredibilityResult result)
    {
        var score = 50; // 基础分

        // 历史重复扣分
        if (result.HasHistoricalDuplicate)
        {
            score -= 20;
        }

        // 逻辑评分加减分
        score += (result.LogicScore - 50) / 5;

        // 资金配合加减分
        score += (result.CapitalScore - 50) / 5;

        return Math.Max(0, Math.Min(100, score));
    }

    private Verdict DetermineVerdict(CredibilityResult result)
    {
        if (result.CredibilityScore >= 70) return Verdict.Real;
        if (result.CredibilityScore <= 30) return Verdict.Fake;
        return Verdict.Uncertain;
    }

    private string GenerateReason(CredibilityResult result)
    {
        var reasons = new List<string>();

        if (result.HasHistoricalDuplicate)
        {
            reasons.Add("存在历史重复事件");
        }

        if (result.LogicScore < 50)
        {
            reasons.Add("逻辑合理性存疑");
        }

        if (result.CapitalScore > 70)
        {
            reasons.Add("存在资金配合嫌疑");
        }

        if (!reasons.Any())
        {
            reasons.Add("未发现明显异常");
        }

        return string.Join("；", reasons);
    }

    private List<string> GenerateRiskWarnings(CredibilityResult result)
    {
        var warnings = new List<string>();

        if (result.HasHistoricalDuplicate)
        {
            warnings.Add("该事件可能为旧闻重炒");
        }

        if (result.LogicScore < 50)
        {
            warnings.Add("事件逻辑存在漏洞，谨慎对待");
        }

        if (result.CapitalScore > 70)
        {
            warnings.Add("可能存在资金配合，注意风险");
        }

        if (result.CredibilityScore < 50)
        {
            warnings.Add("整体可信度较低，建议观望");
        }

        return warnings;
    }

    private double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;

        // 简单的字符相似度计算
        var set1 = text1.ToCharArray().ToHashSet();
        var set2 = text2.ToCharArray().ToHashSet();

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    private string CleanJsonResponse(string response)
    {
        return Common.LLMResponseParser.CleanJsonResponse(response);
    }
}
