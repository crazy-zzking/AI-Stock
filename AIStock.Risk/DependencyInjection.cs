using AIStock.Core.Interfaces;
using AIStock.Risk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Risk;

public static class DependencyInjection
{
    public static IServiceCollection AddRiskServices(this IServiceCollection services)
    {
        services.AddScoped<IRiskEngine, RiskEngineService>();
        services.AddScoped<IPositionSizer, PositionSizerService>();
        services.AddScoped<IStopLossSystem, StopLossSystemService>();
        return services;
    }
}
