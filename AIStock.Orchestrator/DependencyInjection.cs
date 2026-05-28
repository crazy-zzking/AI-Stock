using AIStock.Core.Interfaces;
using AIStock.Orchestrator.Agents;
using Microsoft.Extensions.DependencyInjection;

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
}
