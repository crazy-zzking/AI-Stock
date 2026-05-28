using AIStock.Core.Interfaces;
using AIStock.Feature.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Feature;

public static class DependencyInjection
{
    public static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        services.AddScoped<IFeatureCalculator, FeatureCalculatorService>();
        services.AddScoped<IFeatureStore, FeatureStoreService>();
        services.AddScoped<IMarketStateDetector, MarketStateDetectorService>();
        return services;
    }
}
