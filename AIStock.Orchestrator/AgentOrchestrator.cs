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

    public AgentOrchestrator(IEnumerable<IAgent> agents, ILogger<AgentOrchestrator> logger)
    {
        _logger = logger;

        // 启动时一次性注册所有通过DI注入的Agent，避免并发竞价条件
        foreach (var agent in agents)
        {
            _agents[agent.AgentId] = agent;
            _logger.LogInformation("Registered agent: {AgentId} ({Name})", agent.AgentId, agent.Name);
        }
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
            // 验证所有 Agent 存在
            foreach (var step in workflow.Steps)
            {
                if (!_agents.ContainsKey(step.AgentId))
                {
                    result.Success = false;
                    result.FinalOutput["error"] = $"Agent not found: {step.AgentId}";
                    return result;
                }
            }

            // 拓扑排序：将步骤按依赖关系分组为层级
            var levels = TopologicalSort(workflow.Steps);
            if (levels == null)
            {
                result.Success = false;
                result.FinalOutput["error"] = "Workflow contains circular dependency";
                return result;
            }

            var stepResults = new Dictionary<string, AgentResult>();

            foreach (var level in levels)
            {
                // 同层级并行执行
                var levelTasks = level.Select(step => ExecuteStepAsync(step, stepResults));
                var levelResults = await Task.WhenAll(levelTasks);

                foreach (var (stepId, agentResult) in levelResults)
                {
                    stepResults[stepId] = agentResult;

                    if (!agentResult.Success)
                    {
                        result.Success = false;
                        result.StepResults = stepResults;
                        result.FinalOutput["error"] = $"Step {stepId} failed: {agentResult.Message}";
                        result.TotalExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                        return result;
                    }
                }
            }

            result.Success = true;
            result.StepResults = stepResults;

            var lastStep = workflow.Steps.Last();
            if (stepResults.TryGetValue(lastStep.StepId, out var finalResult))
            {
                result.FinalOutput = finalResult.Output;
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

    private async Task<(string StepId, AgentResult Result)> ExecuteStepAsync(
        WorkflowStep step, Dictionary<string, AgentResult> stepResults)
    {
        var agent = _agents[step.AgentId];

        var task = new AgentTask
        {
            TaskType = step.TaskType,
            Parameters = new Dictionary<string, object>(step.Parameters)
        };

        // 注入依赖步骤的输出
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

        _logger.LogDebug("Executing step {StepId} via agent {AgentId}", step.StepId, step.AgentId);
        var agentResult = await agent.ExecuteAsync(task);
        _logger.LogDebug("Step {StepId} completed: Success={Success}", step.StepId, agentResult.Success);

        return (step.StepId, agentResult);
    }

    /// <summary>
    /// 拓扑排序：将步骤按 DependsOn 分组为层级，同层级步骤无相互依赖，可并行执行。
    /// 返回 null 表示检测到循环依赖。
    /// </summary>
    private static List<List<WorkflowStep>>? TopologicalSort(List<WorkflowStep> steps)
    {
        var stepMap = steps.ToDictionary(s => s.StepId);
        var inDegree = steps.ToDictionary(s => s.StepId, _ => 0);
        var dependents = steps.ToDictionary(s => s.StepId, _ => new List<string>());

        // 计算入度和反向依赖
        foreach (var step in steps)
        {
            foreach (var depId in step.DependsOn)
            {
                if (!stepMap.ContainsKey(depId))
                    continue;
                inDegree[step.StepId]++;
                dependents[depId].Add(step.StepId);
            }
        }

        var levels = new List<List<WorkflowStep>>();
        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var processed = 0;

        while (queue.Count > 0)
        {
            var levelSize = queue.Count;
            var level = new List<WorkflowStep>();

            for (int i = 0; i < levelSize; i++)
            {
                var stepId = queue.Dequeue();
                level.Add(stepMap[stepId]);
                processed++;

                foreach (var dependentId in dependents[stepId])
                {
                    inDegree[dependentId]--;
                    if (inDegree[dependentId] == 0)
                    {
                        queue.Enqueue(dependentId);
                    }
                }
            }

            levels.Add(level);
        }

        // 如果处理数量不等于步骤总数，说明存在循环依赖
        return processed == steps.Count ? levels : null;
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
