using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Services;

/// <summary>
/// 传播链分析服务
/// </summary>
public class SpreadAnalyzerService : ISpreadAnalyzer
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILogger<SpreadAnalyzerService> _logger;

    public SpreadAnalyzerService(
        AIStockDbContext dbContext,
        ILogger<SpreadAnalyzerService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<SpreadAnalysisResult> AnalyzeSpreadAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var result = new SpreadAnalysisResult();

        // 从事件记录中查找相关事件
        var events = await _dbContext.EventRecord
            .Where(e => e.Title.Contains(keyword) || e.Content!.Contains(keyword))
            .OrderBy(e => e.EventTime)
            .ToListAsync(cancellationToken);

        if (!events.Any())
        {
            result.Conclusion = "未找到相关事件";
            return result;
        }

        // 分析首发源
        var firstEvent = events.First();
        result.FirstSource = firstEvent.Source;
        result.FirstTime = firstEvent.EventTime;

        // 构建传播路径
        result.SpreadPath = events.Select(e => new SpreadNode
        {
            Source = e.Source ?? "未知",
            Time = e.EventTime ?? e.CreatedAt,
            Summary = e.Title,
            Influence = CalculateInfluence(e)
        }).ToList();

        // 计算当前热度
        result.CurrentHeat = CalculateCurrentHeat(events);

        // 计算热度斜率
        result.HeatSlope = await CalculateHeatSlopeAsync(keyword, 24, cancellationToken);

        // 判断传播速度
        result.SpreadSpeed = DetermineSpreadSpeed(result);

        // 预计峰值时间
        result.PeakTime = EstimatePeakTime(result);

        // 生成结论
        result.Conclusion = GenerateConclusion(result);

        return result;
    }

    public async Task<double> CalculateHeatSlopeAsync(string keyword, int hours = 24, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow.AddHours(-hours);

        var events = await _dbContext.EventRecord
            .Where(e => (e.Title.Contains(keyword) || e.Content!.Contains(keyword))
                       && e.CreatedAt >= startTime)
            .GroupBy(e => e.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderBy(x => x.Hour)
            .ToListAsync(cancellationToken);

        if (events.Count < 2)
        {
            return 0;
        }

        // 计算线性回归斜率
        var xValues = events.Select((e, i) => (double)i).ToList();
        var yValues = events.Select(e => (double)e.Count).ToList();

        var n = xValues.Count;
        var sumX = xValues.Sum();
        var sumY = yValues.Sum();
        var sumXY = xValues.Zip(yValues, (x, y) => x * y).Sum();
        var sumX2 = xValues.Select(x => x * x).Sum();

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

        return slope;
    }

    private int CalculateInfluence(EventRecordEntity evt)
    {
        var influence = 50; // 基础分

        // 根据事件类型调整
        switch (evt.EventType)
        {
            case "report":
                influence += 10;
                break;
            case "policy":
                influence += 20;
                break;
        }

        // 根据重要性调整
        if (evt.Importance.HasValue)
        {
            influence += evt.Importance.Value;
        }

        return Math.Min(100, influence);
    }

    private int CalculateCurrentHeat(List<EventRecordEntity> events)
    {
        var recentEvents = events.Where(e => e.CreatedAt >= DateTime.UtcNow.AddHours(-24)).ToList();
        var totalEvents = events.Count;

        if (totalEvents == 0) return 0;

        // 近24小时事件占比
        var recentRatio = (double)recentEvents.Count / totalEvents;

        // 基础热度
        var heat = (int)(recentRatio * 100);

        // 根据事件数量调整
        heat += Math.Min(20, recentEvents.Count * 2);

        return Math.Min(100, heat);
    }

    private string DetermineSpreadSpeed(SpreadAnalysisResult result)
    {
        if (result.HeatSlope > 5) return "viral";
        if (result.HeatSlope > 2) return "fast";
        if (result.HeatSlope > 0) return "medium";
        return "slow";
    }

    private DateTime? EstimatePeakTime(SpreadAnalysisResult result)
    {
        if (result.HeatSlope <= 0) return null;

        // 简单估算：假设热度会持续增长2-4小时
        return DateTime.UtcNow.AddHours(3);
    }

    private string GenerateConclusion(SpreadAnalysisResult result)
    {
        var conclusions = new List<string>();

        conclusions.Add($"首发源：{result.FirstSource ?? "未知"}");
        conclusions.Add($"传播速度：{GetSpeedDescription(result.SpreadSpeed)}");
        conclusions.Add($"当前热度：{result.CurrentHeat}%");

        if (result.HeatSlope > 2)
        {
            conclusions.Add("热度持续上升，需关注");
        }
        else if (result.HeatSlope < -2)
        {
            conclusions.Add("热度正在消退");
        }

        return string.Join("；", conclusions);
    }

    private string GetSpeedDescription(string speed)
    {
        return speed switch
        {
            "viral" => "病毒式传播",
            "fast" => "快速传播",
            "medium" => "正常传播",
            "slow" => "缓慢传播",
            _ => "未知"
        };
    }
}
