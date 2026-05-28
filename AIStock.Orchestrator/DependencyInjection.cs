using AIStock.Core.Interfaces;
using AIStock.Orchestrator.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator;

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestratorServices(this IServiceCollection services)
    {
        // Agent注册为Scoped，同时注册为IAgent以便AgentOrchestrator通过IEnumerable<IAgent>收集
        services.AddScoped<ResearchAgent>();
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<ResearchAgent>());
        services.AddScoped<AlphaAgent>();
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<AlphaAgent>());
        services.AddScoped<RiskAgent>();
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<RiskAgent>());

        // AgentOrchestrator为Scoped，通过构造函数注入IEnumerable<IAgent>自动收集所有Agent
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<AutonomousDecisionSystem>();
        return services;
    }

    /// <summary>
    /// （可选）为指定Agent启用反射式自我反思。调用后将用ReflectiveAgent包装器替换原IAgent注册。
    /// </summary>
    public static IServiceCollection AddReflectiveAgent<TAgent>(this IServiceCollection services)
        where TAgent : class, IAgent
    {
        // 找到并移除原有IAgent注册中返回TAgent的descriptor
        var toRemove = services
            .Where(d => d.ServiceType == typeof(IAgent) && d.Lifetime == ServiceLifetime.Scoped)
            .Where(d =>
            {
                // 通过检查factory能否产生TAgent来判断
                if (d.ImplementationFactory != null)
                {
                    try
                    {
                        // 构造一个空的ServiceProvider测试
                        return d.ImplementationFactory.Method.ReturnType == typeof(TAgent) ||
                               d.ImplementationFactory.Method.ReturnType.IsAssignableTo(typeof(TAgent));
                    }
                    catch { return false; }
                }
                return d.ImplementationType == typeof(TAgent);
            })
            .ToList();

        foreach (var desc in toRemove)
            services.Remove(desc);

        // 重新注册为反射式包装
        services.AddScoped<IAgent>(sp =>
        {
            var inner = sp.GetRequiredService<TAgent>();
            var llmService = sp.GetRequiredService<ILLMService>();
            var logger = sp.GetRequiredService<ILogger<ReflectiveAgent>>();
            return new ReflectiveAgent(inner, llmService, logger, maxReflections: 2);
        });

        return services;
    }
}
