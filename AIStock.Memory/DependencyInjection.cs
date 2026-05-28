using AIStock.Memory.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIStock.Memory;

/// <summary>
/// Memory模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加Agent Memory服务
    /// </summary>
    public static IServiceCollection AddMemoryServices(this IServiceCollection services)
    {
        services.TryAddScoped<IAgentMemory, AgentMemoryService>();
        return services;
    }
}
