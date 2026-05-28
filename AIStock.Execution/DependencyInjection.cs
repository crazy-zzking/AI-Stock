using AIStock.Core.Interfaces;
using AIStock.Execution.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Execution;

public static class DependencyInjection
{
    public static IServiceCollection AddExecutionServices(this IServiceCollection services)
    {
        services.AddScoped<ISignalGenerator, SignalGeneratorService>();
        services.AddSingleton<IOrderManager, OrderManagerService>();
        services.AddSingleton<IPositionManager, PositionManagerService>();
        return services;
    }
}
