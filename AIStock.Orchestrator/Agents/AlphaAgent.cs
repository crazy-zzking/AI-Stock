using AIStock.Core.Interfaces;
using AIStock.Core.Models;
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
    private readonly ILogger<AlphaAgent> _logger;

    public AlphaAgent(
        IAlphaEngine alphaEngine,
        IFeatureCalculator featureCalculator,
        IDataProviderResolver dataProviderResolver,
        ILogger<AlphaAgent> logger)
    {
        _alphaEngine = alphaEngine;
        _featureCalculator = featureCalculator;
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    public string AgentId => "alpha-agent";
    public string Name => "Alpha Agent";
    public string Type => "alpha";

    public async Task<AgentResult> ExecuteAsync(AgentTask task)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var taskType = task.TaskType.ToLower();
            return taskType switch
            {
                "generate-signal" => await GenerateSignalAsync(task),
                "merge-signals" => await MergeSignalsAsync(task),
                _ => new AgentResult { Success = false, Message = $"Unknown task type: {taskType}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alpha agent execution failed");
            return new AgentResult
            {
                Success = false,
                Message = ex.Message,
                ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
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

        var signals = new List<TradeSignal>();

        if (indicators.MA?.MA5 > indicators.MA?.MA20 && currentPrice > indicators.MA?.MA5)
        {
            signals.Add(new TradeSignal
            {
                Code = code,
                SignalType = "buy",
                Strength = 70,
                Price = currentPrice,
                StrategyName = "MASignal",
                Reason = "MA5 > MA20, price above MA5"
            });
        }

        if (indicators.RSI?.RSI12 < 30)
        {
            signals.Add(new TradeSignal
            {
                Code = code,
                SignalType = "buy",
                Strength = 65,
                Price = currentPrice,
                StrategyName = "RSISignal",
                Reason = "RSI oversold"
            });
        }

        var result = new Dictionary<string, object>
        {
            ["code"] = code,
            ["currentPrice"] = currentPrice,
            ["signals"] = signals
        };

        return new AgentResult
        {
            Success = true,
            Output = result,
            Message = $"Generated {signals.Count} signals for {code}",
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
