using AIStock.Core.Interfaces;
using AIStock.Orchestrator.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Orchestrator;

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestratorServices(this IServiceCollection services)
    {
        services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<ResearchAgent>();
        services.AddScoped<AlphaAgent>();
        services.AddScoped<RiskAgent>();
        services.AddScoped<AutonomousDecisionSystem>();
        return services;
    }
}
