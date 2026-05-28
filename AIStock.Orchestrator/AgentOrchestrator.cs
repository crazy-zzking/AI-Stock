using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator;

/// <summary>
/// Agent编排器实现
/// </summary>
public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly Dictionary<string, IAgent> _agents = new();
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(ILogger<AgentOrchestrator> logger)
    {
        _logger = logger;
    }

    public void RegisterAgent(IAgent agent)
    {
        _agents[agent.AgentId] = agent;
        _logger.LogInformation("Registered agent: {AgentId} ({Name})", agent.AgentId, agent.Name);
    }

    public IAgent? GetAgent(string agentId)
    {
        _agents.TryGetValue(agentId, out var agent);
        return agent;
    }

    public async Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        var startTime = DateTime.UtcNow;
        var result = new WorkflowResult
        {
            WorkflowId = workflow.WorkflowId
        };

        try
        {
            var completedSteps = new HashSet<string>();
            var stepResults = new Dictionary<string, AgentResult>();

            foreach (var step in workflow.Steps)
            {
                if (!step.DependsOn.All(d => completedSteps.Contains(d)))
                {
                    continue;
                }

                var agent = GetAgent(step.AgentId);
                if (agent == null)
                {
                    result.Success = false;
                    result.FinalOutput["error"] = $"Agent not found: {step.AgentId}";
                    return result;
                }

                var task = new AgentTask
                {
                    TaskType = step.TaskType,
                    Parameters = new Dictionary<string, object>(step.Parameters)
                };

                foreach (var dep in step.DependsOn)
                {
                    if (stepResults.TryGetValue(dep, out var depResult))
                    {
                        foreach (var output in depResult.Output)
                        {
                            task.Parameters[$"dep_{dep}_{output.Key}"] = output.Value;
                        }
                    }
                }

                var agentResult = await agent.ExecuteAsync(task);
                stepResults[step.StepId] = agentResult;
                completedSteps.Add(step.StepId);

                if (!agentResult.Success)
                {
                    result.Success = false;
                    result.StepResults = stepResults;
                    result.FinalOutput["error"] = $"Step {step.StepId} failed: {agentResult.Message}";
                    return result;
                }
            }

            result.Success = true;
            result.StepResults = stepResults;

            var lastResult = stepResults.Values.LastOrDefault();
            if (lastResult != null)
            {
                result.FinalOutput = lastResult.Output;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed");
            result.Success = false;
            result.FinalOutput["error"] = ex.Message;
        }

        result.TotalExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        return result;
    }

    public async Task<List<AgentStatus>> GetAllAgentStatusAsync()
    {
        var statuses = new List<AgentStatus>();

        foreach (var agent in _agents.Values)
        {
            var status = await agent.GetStatusAsync();
            statuses.Add(status);
        }

        return statuses;
    }
}
