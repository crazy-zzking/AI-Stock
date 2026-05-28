using AIStock.Core.Interfaces;
using AIStock.Strategy.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Strategy;

public static class DependencyInjection
{
    public static IServiceCollection AddStrategyServices(this IServiceCollection services)
    {
        services.AddScoped<IStrategy, MABreakoutStrategy>();
        services.AddScoped<IStrategy, GridTradingStrategy>();
        services.AddScoped<IStrategy, PairTradingStrategy>();
        services.AddScoped<IBacktestEngine, BacktestEngineService>();
        services.AddScoped<IAlphaEngine, AlphaEngineService>();
        services.AddScoped<IPortfolioEngine, PortfolioEngineService>();
        return services;
    }
}
