using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator;

/// <summary>
/// 自主决策系统 - 自动分析市场并生成交易决策
/// </summary>
public class AutonomousDecisionSystem
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<AutonomousDecisionSystem> _logger;

    public AutonomousDecisionSystem(
        IAgentOrchestrator orchestrator,
        IDataProviderResolver dataProviderResolver,
        ILogger<AutonomousDecisionSystem> logger)
    {
        _orchestrator = orchestrator;
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    /// <summary>
    /// 执行自主决策流程
    /// </summary>
    public async Task<DecisionResult> MakeDecisionAsync(DecisionRequest request)
    {
        var startTime = DateTime.UtcNow;
        var result = new DecisionResult
        {
            Code = request.Code
        };

        try
        {
            var analyzeResult = await ExecuteAnalysisAsync(request.Code);
            if (!analyzeResult.Success)
            {
                result.Success = false;
                result.Message = $"Analysis failed: {analyzeResult.Message}";
                return result;
            }

            var signalResult = await ExecuteSignalGenerationAsync(request.Code);
            if (!signalResult.Success)
            {
                result.Success = false;
                result.Message = $"Signal generation failed: {signalResult.Message}";
                return result;
            }

            var riskResult = await ExecuteRiskCheckAsync(signalResult, request.TotalCapital);
            
            result.Success = true;
            result.Analysis = analyzeResult.Output;
            result.Signals = signalResult.Output;
            result.RiskCheck = riskResult?.Output;
            result.Message = "Decision completed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autonomous decision failed for {Code}", request.Code);
            result.Success = false;
            result.Message = ex.Message;
        }

        result.ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        return result;
    }

    /// <summary>
    /// 批量决策
    /// </summary>
    public async Task<List<DecisionResult>> MakeBatchDecisionAsync(List<string> codes, decimal totalCapital)
    {
        var results = new List<DecisionResult>();

        foreach (var code in codes)
        {
            var request = new DecisionRequest
            {
                Code = code,
                TotalCapital = totalCapital
            };

            var result = await MakeDecisionAsync(request);
            results.Add(result);
        }

        return results;
    }

    private async Task<AgentResult> ExecuteAnalysisAsync(string code)
    {
        var agent = _orchestrator.GetAgent("research-agent");
        if (agent == null)
        {
            return new AgentResult { Success = false, Message = "Research agent not found" };
        }

        var task = new AgentTask
        {
            TaskType = "analyze-stock",
            Parameters = new Dictionary<string, object> { ["code"] = code }
        };

        return await agent.ExecuteAsync(task);
    }

    private async Task<AgentResult> ExecuteSignalGenerationAsync(string code)
    {
        var agent = _orchestrator.GetAgent("alpha-agent");
        if (agent == null)
        {
            return new AgentResult { Success = false, Message = "Alpha agent not found" };
        }

        var task = new AgentTask
        {
            TaskType = "generate-signal",
            Parameters = new Dictionary<string, object> { ["code"] = code }
        };

        return await agent.ExecuteAsync(task);
    }

    private async Task<AgentResult?> ExecuteRiskCheckAsync(AgentResult signalResult, decimal totalCapital)
    {
        var agent = _orchestrator.GetAgent("risk-agent");
        if (agent == null)
        {
            return null;
        }

        var task = new AgentTask
        {
            TaskType = "check-risk",
            Parameters = new Dictionary<string, object>
            {
                ["totalCapital"] = totalCapital
            }
        };

        return await agent.ExecuteAsync(task);
    }
}

/// <summary>
/// 决策请求
/// </summary>
public class DecisionRequest
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 总资金
    /// </summary>
    public decimal TotalCapital { get; set; }
}

/// <summary>
/// 决策结果
/// </summary>
public class DecisionResult
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 分析结果
    /// </summary>
    public Dictionary<string, object> Analysis { get; set; } = new();

    /// <summary>
    /// 信号结果
    /// </summary>
    public Dictionary<string, object> Signals { get; set; } = new();

    /// <summary>
    /// 风控结果
    /// </summary>
    public Dictionary<string, object>? RiskCheck { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 执行时间
    /// </summary>
    public long ExecutionTime { get; set; }
}
