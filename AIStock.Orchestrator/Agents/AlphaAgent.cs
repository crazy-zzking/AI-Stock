using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Memory;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator.Agents;

/// <summary>
/// Alpha Agent - 负责交易信号生成
/// </summary>
public class AlphaAgent : IAgent
{
    private readonly IAlphaEngine _alphaEngine;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IEnumerable<IStrategy> _strategies;
    private readonly ILogger<AlphaAgent> _logger;
    private readonly IAgentMemory? _memory;

    public AlphaAgent(
        IAlphaEngine alphaEngine,
        IFeatureCalculator featureCalculator,
        IDataProviderResolver dataProviderResolver,
        IEnumerable<IStrategy> strategies,
        ILogger<AlphaAgent> logger,
        IAgentMemory? memory = null)
    {
        _alphaEngine = alphaEngine;
        _featureCalculator = featureCalculator;
        _dataProviderResolver = dataProviderResolver;
        _strategies = strategies;
        _logger = logger;
        _memory = memory;
    }

    public string AgentId => "alpha-agent";
    public string Name => "Alpha Agent";
    public string Type => "alpha";

    public async Task<AgentResult> ExecuteAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;
        AgentResult result;

        try
        {
            var taskType = task.TaskType.ToLower();
            result = taskType switch
            {
                "generate-signal" => await GenerateSignalAsync(task),
                "merge-signals" => await MergeSignalsAsync(task),
                _ => new AgentResult { Success = false, Message = $"Unknown task type: {taskType}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alpha agent execution failed");
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

    private async Task<AgentResult> GenerateSignalAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;
        var code = task.Parameters.GetValueOrDefault("code")?.ToString() ?? "";

        var provider = _dataProviderResolver.GetDefaultProvider();
        var klines = await provider.GetKlinesAsync(code, Core.Enums.KlineInterval.Daily, 100);

        if (klines == null || klines.Count < 60)
        {
            return new AgentResult
            {
                Success = false,
                Message = $"Insufficient data for {code}"
            };
        }

        var indicators = _featureCalculator.CalculateAll(code, klines);
        var currentPrice = klines.Last().Close;

        // 运行所有已注册策略，收集原始信号（含买/卖，带止损止盈）
        var rawSignals = new List<TradeSignal>();
        foreach (var strategy in _strategies)
        {
            try
            {
                var signal = await strategy.GenerateSignalAsync(code, klines, indicators);
                if (signal != null && signal.SignalType != Core.Enums.SignalType.Hold)
                    rawSignals.Add(signal);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Strategy {Strategy} failed for {Code}", strategy.Name, code);
            }
        }

        // 多策略融合为单一综合信号（含多空判定）
        var composite = await _alphaEngine.GenerateCompositeSignalAsync(code, rawSignals);

        // 仅当综合信号可执行（买/卖）时才作为可下单信号输出，Hold 不下单
        var actionable = composite.SignalType != Core.Enums.SignalType.Hold
            ? new List<TradeSignal> { composite }
            : new List<TradeSignal>();

        var result = new Dictionary<string, object>
        {
            ["code"] = code,
            ["currentPrice"] = currentPrice,
            ["signals"] = actionable,
            ["rawSignals"] = rawSignals,
            ["composite"] = composite
        };

        return new AgentResult
        {
            Success = true,
            Output = result,
            Message = $"{_strategies.Count()} strategies → {rawSignals.Count} raw signals → composite {composite.SignalType} (strength {composite.Strength})",
            ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
        };
    }

    private async Task<AgentResult> MergeSignalsAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;

        var signals = new List<TradeSignal>();
        if (task.Parameters.ContainsKey("signals"))
        {
            var signalList = task.Parameters["signals"] as List<TradeSignal>;
            if (signalList != null)
                signals = signalList;
        }

        var mergedSignal = await _alphaEngine.MergeSignalsAsync(signals);

        return new AgentResult
        {
            Success = true,
            Output = new Dictionary<string, object>
            {
                ["mergedSignal"] = mergedSignal
            },
            Message = "Signals merged successfully",
            ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
        };
    }
}
