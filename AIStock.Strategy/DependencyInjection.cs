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
        services.AddScoped<IStrategy>(sp => sp.GetRequiredService<MABreakoutStrategy>());
        services.AddScoped<IBacktestEngine, BacktestEngineService>();
        services.AddScoped<IAlphaEngine, AlphaEngineService>();
        services.AddScoped<IPortfolioEngine, PortfolioEngineService>();
        return services;
    }
}
