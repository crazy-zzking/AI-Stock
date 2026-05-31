using AIStock.Selection.Narration;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Selection;

public static class DependencyInjection
{
    public static IServiceCollection AddSelectionServices(this IServiceCollection services)
    {
        // 默认规则叙述器；如需 LLM 增强，替换为 LlmLogicNarrator 实现即可
        services.AddScoped<ILogicNarrator, RuleLogicNarrator>();
        services.AddScoped<StockSelectionEngine>();
        services.AddScoped<StockSelectionService>();
        return services;
    }
}
