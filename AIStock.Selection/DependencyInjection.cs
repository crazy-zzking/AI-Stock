using AIStock.Selection.Backtest;
using AIStock.Selection.Narration;
using AIStock.Selection.Review;
using AIStock.Selection.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIStock.Selection;

public static class DependencyInjection
{
    public static IServiceCollection AddSelectionServices(this IServiceCollection services)
    {
        // 默认规则叙述器；如需 LLM 增强，替换为 LlmLogicNarrator 实现即可
        services.AddScoped<ILogicNarrator, RuleLogicNarrator>();
        services.AddScoped<StockSelectionEngine>();
        services.AddScoped<SelectionConfigService>();
        services.AddScoped<StrategyDefinitionService>();

        // 选股策略池（市场状态决定启用哪个；默认低吸）
        services.AddScoped<ISelectionStrategy, LowDipStrategy>();
        services.AddScoped<ISelectionStrategy, TrendStrategy>();
        services.AddScoped<ISelectionStrategy, ThemeStrategy>();
        services.AddScoped<ISelectionStrategy, KPatternStrategy>();
        services.AddScoped<ISelectionStrategy, HotMoneyStrategy>();
        services.AddScoped<ISelectionStrategy, AmbushStrategy>();
        services.AddScoped<ISelectionStrategy, WhisperThemeStrategy>();
        // 合并"内置 + 数据库自建"策略，供运行时解析/列出/回测
        services.AddScoped<ISelectionStrategyProvider, SelectionStrategyProvider>();

        services.AddScoped<StockSelectionService>();
        services.AddScoped<SelectionDailyService>();
        services.AddScoped<BacktestService>();
        services.AddScoped<ReplayBacktestService>();
        // 选股信号前向绩效（策略记分板）：Worker 每日补算，Web 查询聚合
        services.AddScoped<Performance.SelectionPerformanceService>();

        // LLM 复评（异步附加步骤）。队列默认空实现；Web 层用 Channel 版 + 后台消费替换。
        services.AddScoped<SelectionReviewService>();
        services.TryAddSingleton<ISelectionReviewQueue, NullSelectionReviewQueue>();
        return services;
    }
}
