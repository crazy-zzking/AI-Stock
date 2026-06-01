using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// Agent接口
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Agent ID
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Agent名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Agent类型
    /// </summary>
    string Type { get; }

    /// <summary>
    /// 执行任务
    /// </summary>
    Task<AgentResult> ExecuteAsync(AgentTask task);

    /// <summary>
    /// 获取状态
    /// </summary>
    Task<AgentStatus> GetStatusAsync();
}

/// <summary>
/// Agent任务
/// </summary>
public class AgentTask
{
    /// <summary>
    /// 任务ID
    /// </summary>
    public string TaskId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 任务类型
    /// </summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 输入参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateTime { get; set; } = DateTime.Now;
}

/// <summary>
/// Agent结果
/// </summary>
public class AgentResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 输出数据
    /// </summary>
    public Dictionary<string, object> Output { get; set; } = new();

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTime { get; set; }
}

/// <summary>
/// Agent状态
/// </summary>
public class AgentStatus
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// 是否在线
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    /// 当前任务数
    /// </summary>
    public int CurrentTasks { get; set; }

    /// <summary>
    /// 已完成任务数
    /// </summary>
    public int CompletedTasks { get; set; }

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveTime { get; set; }
}

/// <summary>
/// Multi-Agent编排器接口
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// 注册Agent
    /// </summary>
    void RegisterAgent(IAgent agent);

    /// <summary>
    /// 获取Agent
    /// </summary>
    IAgent? GetAgent(string agentId);

    /// <summary>
    /// 执行工作流
    /// </summary>
    Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition workflow);

    /// <summary>
    /// 获取所有Agent状态
    /// </summary>
    Task<List<AgentStatus>> GetAllAgentStatusAsync();
}

/// <summary>
/// 工作流定义
/// </summary>
public class WorkflowDefinition
{
    /// <summary>
    /// 工作流ID
    /// </summary>
    public string WorkflowId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 工作流名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 步骤列表
    /// </summary>
    public List<WorkflowStep> Steps { get; set; } = new();

    /// <summary>
    /// 输入参数
    /// </summary>
    public Dictionary<string, object> Input { get; set; } = new();
}

/// <summary>
/// 工作流步骤
/// </summary>
public class WorkflowStep
{
    /// <summary>
    /// 步骤ID
    /// </summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// 任务类型
    /// </summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 依赖步骤
    /// </summary>
    public List<string> DependsOn { get; set; } = new();
}

/// <summary>
/// 工作流结果
/// </summary>
public class WorkflowResult
{
    /// <summary>
    /// 工作流ID
    /// </summary>
    public string WorkflowId { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 步骤结果
    /// </summary>
    public Dictionary<string, AgentResult> StepResults { get; set; } = new();

    /// <summary>
    /// 最终输出
    /// </summary>
    public Dictionary<string, object> FinalOutput { get; set; } = new();

    /// <summary>
    /// 总执行时间（毫秒）
    /// </summary>
    public long TotalExecutionTime { get; set; }
}
