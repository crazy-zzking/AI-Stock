using AIStock.Selection.Backtest;
using AIStock.Selection.Narration;
using AIStock.Selection.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Selection;

public static class DependencyInjection
{
    public static IServiceCollection AddSelectionServices(this IServiceCollection services)
    {
        // 默认规则叙述器；如需 LLM 增强，替换为 LlmLogicNarrator 实现即可
        services.AddScoped<ILogicNarrator, RuleLogicNarrator>();
        services.AddScoped<StockSelectionEngine>();
        services.AddScoped<SelectionConfigService>();

        // 选股策略池（市场状态决定启用哪个；默认低吸）
        services.AddScoped<ISelectionStrategy, LowDipStrategy>();
        services.AddScoped<ISelectionStrategy, TrendStrategy>();
        services.AddScoped<ISelectionStrategy, ThemeStrategy>();

        services.AddScoped<StockSelectionService>();
        services.AddScoped<BacktestService>();
        services.AddScoped<ReplayBacktestService>();
        return services;
    }
}
