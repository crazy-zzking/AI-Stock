using AIStock.Prompt.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIStock.Prompt;

/// <summary>
/// Prompt模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加Prompt Registry服务
    /// </summary>
    public static IServiceCollection AddPromptServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IPromptRegistry, PromptRegistryService>();
        return services;
    }
}
