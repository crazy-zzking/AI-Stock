using AIStock.Core.Interfaces;

namespace AIStock.Memory;

/// <summary>
/// Agent记忆接口 — 持久化Agent分析记录，支持跨会话回溯
/// </summary>
public interface IAgentMemory
{
    /// <summary>
    /// 保存Agent执行记录
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="task">执行的任务</param>
    /// <param name="result">执行结果</param>
    Task SaveAsync(string agentId, AgentTask task, AgentResult result);

    /// <summary>
    /// 获取指定Agent对指定股票的历史分析记录
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="stockCode">股票代码</param>
    /// <param name="count">返回记录数</param>
    Task<List<AgentMemoryRecord>> GetHistoryAsync(string agentId, string stockCode, int count = 10);

    /// <summary>
    /// 获取指定Agent对指定股票的最新一条分析记录
    /// </summary>
    Task<AgentMemoryRecord?> GetLatestAsync(string agentId, string stockCode);

    /// <summary>
    /// 获取Agent最近的所有分析记录
    /// </summary>
    Task<List<AgentMemoryRecord>> GetRecentAsync(string agentId, int count = 20);
}

/// <summary>
/// Agent记忆记录（领域模型）
/// </summary>
public class AgentMemoryRecord
{
    public long Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? KeyMetrics { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long ExecutionTimeMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
