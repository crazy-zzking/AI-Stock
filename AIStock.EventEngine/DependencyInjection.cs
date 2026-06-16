using AIStock.EventEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.EventEngine;

/// <summary>
/// 事件引擎模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加事件引擎服务
    /// </summary>
    public static IServiceCollection AddEventEngineServices(this IServiceCollection services)
    {
        services.AddScoped<EventEngineService>();
        services.AddSingleton<GraphPromotionService>();
        return services;
    }
}
