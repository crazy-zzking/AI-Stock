using AIStock.Core.Interfaces;
using AIStock.Strategy.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Strategy;

public static class DependencyInjection
{
    public static IServiceCollection AddStrategyServices(this IServiceCollection services)
    {
        services.AddScoped<MABreakoutStrategy>();
        services.AddScoped<GridTradingStrategy>();
        services.AddScoped<PairTradingStrategy>();
        // 全部策略注册为 IStrategy，供 IEnumerable<IStrategy> 注入遍历
        services.AddScoped<IStrategy>(sp => sp.GetRequiredService<MABreakoutStrategy>());
        services.AddScoped<IStrategy>(sp => sp.GetRequiredService<GridTradingStrategy>());
        services.AddScoped<IStrategy>(sp => sp.GetRequiredService<PairTradingStrategy>());
        services.AddScoped<IBacktestEngine, BacktestEngineService>();
        services.AddScoped<IAlphaEngine, AlphaEngineService>();
        services.AddScoped<IPortfolioEngine, PortfolioEngineService>();
        return services;
    }
}
