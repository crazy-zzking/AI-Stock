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

    /// <summary>
    /// 添加LLM服务（含Prompt Registry集成）
    /// </summary>
    public static IServiceCollection AddLLMServicesWithPrompt(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddScoped<ILLMProvider, OpenAICompatibleProvider>();
        services.AddScoped<ILLMService>(sp =>
        {
            var dbContext = sp.GetRequiredService<AIStock.Infrastructure.Database.Context.AIStockDbContext>();
            var llmProvider = sp.GetRequiredService<ILLMProvider>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LLMService>>();

            // 尝试获取IPromptRegistry（可选）
            var promptRegistry = sp.GetService<AIStock.Prompt.IPromptRegistry>();
            return new LLMService(dbContext, llmProvider, cache, logger, promptRegistry);
        });
        return services;
    }
}
