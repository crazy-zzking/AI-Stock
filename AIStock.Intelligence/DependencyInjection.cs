using AIStock.Core.Interfaces;
using AIStock.Intelligence.Collectors;
using AIStock.Intelligence.Common;
using AIStock.Intelligence.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Intelligence;

/// <summary>
/// 情报模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加情报服务
    /// </summary>
    public static IServiceCollection AddIntelligenceServices(this IServiceCollection services)
    {
        // 通用工具
        services.AddSingleton<PlaywrightHelper>();

        // 采集器
        services.AddScoped<IReportCollector, EastmoneyReportCollector>();
        services.AddScoped<INewsCollector, NewsCollectorService>();
        services.AddScoped<IKnowledgeStarCollector, KnowledgeStarCollectorService>();

        // 分析服务
        services.AddScoped<IReportAnalyzer, ReportAnalyzerAgent>();
        services.AddScoped<IPolicyAnalyzer, PolicyAnalyzerAgent>();
        services.AddScoped<IEventExtractor, EventExtractorService>();
        services.AddScoped<ISentimentAnalyzer, SentimentAnalysisService>();
        services.AddScoped<IIntensityScorer, IntensityScorerService>();
        services.AddScoped<ICredibilityAnalyzer, CredibilityAnalyzerService>();
        services.AddScoped<ISpreadAnalyzer, SpreadAnalyzerService>();
        services.AddScoped<IEssayAnalyzer, EssayAnalyzerService>();

        return services;
    }
}
