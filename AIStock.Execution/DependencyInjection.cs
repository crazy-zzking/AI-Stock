using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Execution.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Execution;

public static class DependencyInjection
{
    public static IServiceCollection AddExecutionServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
            services.Configure<TradingGuardOptions>(configuration.GetSection(TradingGuardOptions.SectionName));
        else
            services.Configure<TradingGuardOptions>(_ => { }); // 使用默认值（DryRun）

        // 闸门状态持久化（kill-switch/日单数/个股暂停跨重启保持）；store 故障时闸门自动降级纯内存
        services.AddSingleton<ITradingGateStore, DbTradingGateStore>();
        services.AddSingleton<ITradingGate, TradingGate>();
        services.AddScoped<ISignalGenerator, SignalGeneratorService>();
        services.AddSingleton<IOrderManager, OrderManagerService>();
        services.AddSingleton<IPositionManager, PositionManagerService>();
        return services;
    }
}
