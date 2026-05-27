using AIStock.Core.Interfaces;
using AIStock.LLM.Providers;
using AIStock.LLM.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.LLM;

/// <summary>
/// LLM模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加LLM服务
    /// </summary>
    public static IServiceCollection AddLLMServices(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddScoped<ILLMProvider, OpenAICompatibleProvider>();
        services.AddScoped<ILLMService, LLMService>();
        return services;
    }
}
