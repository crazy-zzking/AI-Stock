using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Memory;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator.Agents;

/// <summary>
/// Risk Agent - 负责风险控制
/// </summary>
public class RiskAgent : IAgent
{
    private readonly IRiskEngine _riskEngine;
    private readonly IStopLossSystem _stopLossSystem;
    private readonly ILogger<RiskAgent> _logger;
    private readonly IAgentMemory? _memory;

    public RiskAgent(
        IRiskEngine riskEngine,
        IStopLossSystem stopLossSystem,
        ILogger<RiskAgent> logger,
        IAgentMemory? memory = null)
    {
        _riskEngine = riskEngine;
        _stopLossSystem = stopLossSystem;
        _logger = logger;
        _memory = memory;
    }

    public string AgentId => "risk-agent";
    public string Name => "Risk Agent";
    public string Type => "risk";

    public async Task<AgentResult> ExecuteAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;
        AgentResult result;

        try
        {
            var taskType = task.TaskType.ToLower();
            result = taskType switch
            {
                "check-risk" => await CheckRiskAsync(task),
                "calculate-stop-loss" => await CalculateStopLossAsync(task),
                _ => new AgentResult { Success = false, Message = $"Unknown task type: {taskType}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Risk agent execution failed");
            result = new AgentResult
            {
                Success = false,
                Message = ex.Message,
                ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }

        if (_memory != null)
        {
            _ = _memory.SaveAsync(AgentId, task, result);
        }

        return result;
    }

    public Task<AgentStatus> GetStatusAsync()
    {
        return Task.FromResult(new AgentStatus
        {
            AgentId = AgentId,
            IsOnline = true,
            LastActiveTime = DateTime.UtcNow
        });
    }

    private async Task<AgentResult> CheckRiskAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;

        TradeSignal? signal = null;
        List<PortfolioPosition> positions = new();
        decimal totalCapital = 0;

        if (task.Parameters.ContainsKey("signal"))
            signal = task.Parameters["signal"] as TradeSignal;
        if (task.Parameters.ContainsKey("positions"))
            positions = task.Parameters["positions"] as List<PortfolioPosition> ?? new();
        if (task.Parameters.ContainsKey("totalCapital"))
            totalCapital = Convert.ToDecimal(task.Parameters["totalCapital"]);

        if (signal == null)
        {
            return new AgentResult
            {
                Success = false,
                Message = "Missing signal parameter"
            };
        }

        var result = await _riskEngine.CheckRiskAsync(signal, positions, totalCapital);

        return new AgentResult
        {
            Success = true,
            Output = new Dictionary<string, object>
            {
                ["riskCheckResult"] = result
            },
            Message = result.Passed ? "Risk check passed" : $"Risk check failed: {result.Suggestion}",
            ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
        };
    }

    private Task<AgentResult> CalculateStopLossAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;

        TradeSignal? signal = null;
        List<KlineData> klines = new();

        if (task.Parameters.ContainsKey("signal"))
            signal = task.Parameters["signal"] as TradeSignal;
        if (task.Parameters.ContainsKey("klines"))
            klines = task.Parameters["klines"] as List<KlineData> ?? new();

        if (signal == null || klines.Count == 0)
        {
            return Task.FromResult(new AgentResult
            {
                Success = false,
                Message = "Missing parameters"
            });
        }

        var stopLoss = _stopLossSystem.CalculateStopLoss(signal, klines, Core.Enums.StopLossMode.ATR);
        var takeProfit = _stopLossSystem.CalculateTakeProfit(signal, klines, Core.Enums.StopLossMode.ATR);

        return Task.FromResult(new AgentResult
        {
            Success = true,
            Output = new Dictionary<string, object>
            {
                ["stopLoss"] = stopLoss,
                ["takeProfit"] = takeProfit
            },
            Message = "Stop loss calculated",
            ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
        });
    }
}
