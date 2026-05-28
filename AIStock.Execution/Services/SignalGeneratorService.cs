using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 信号生成器实现
/// </summary>
public class SignalGeneratorService : ISignalGenerator
{
    private readonly IAlphaEngine _alphaEngine;
    private readonly IRiskEngine _riskEngine;
    private readonly ILogger<SignalGeneratorService> _logger;

    public SignalGeneratorService(
        IAlphaEngine alphaEngine,
        IRiskEngine riskEngine,
        ILogger<SignalGeneratorService> logger)
    {
        _alphaEngine = alphaEngine;
        _riskEngine = riskEngine;
        _logger = logger;
    }

    public async Task<TradeSignal> GenerateFinalSignalAsync(string code, List<TradeSignal> signals, List<PortfolioPosition> positions, decimal totalCapital)
    {
        var compositeSignal = await _alphaEngine.GenerateCompositeSignalAsync(code, signals);

        if (compositeSignal.SignalType == "hold")
        {
            return compositeSignal;
        }

        var riskCheck = await _riskEngine.CheckRiskAsync(compositeSignal, positions, totalCapital);

        if (!riskCheck.Passed)
        {
            _logger.LogWarning("Risk check failed for {Code}: {Suggestion}", code, riskCheck.Suggestion);

            if (riskCheck.RiskLevel == "critical")
            {
                compositeSignal.SignalType = SignalType.Hold.ToString().ToLower();
                compositeSignal.Reason = $"风控拒绝: {riskCheck.Suggestion}";
                compositeSignal.Strength = 0;
            }
            else
            {
                compositeSignal.Strength = Math.Max(0, compositeSignal.Strength - 20);
                compositeSignal.Reason += $" (风控警告: {riskCheck.Suggestion})";
            }
        }

        return compositeSignal;
    }

    public async Task<List<TradeSignal>> GenerateBatchSignalsAsync(List<string> codes, List<PortfolioPosition> positions, decimal totalCapital)
    {
        var results = new List<TradeSignal>();

        foreach (var code in codes)
        {
            try
            {
                var signal = await GenerateFinalSignalAsync(code, new List<TradeSignal>(), positions, totalCapital);
                if (signal.SignalType != "hold")
                {
                    results.Add(signal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate signal for {Code}", code);
            }
        }

        return results.OrderByDescending(s => s.Strength).ToList();
    }
}

public interface ISignalGenerator
{
    Task<TradeSignal> GenerateFinalSignalAsync(string code, List<TradeSignal> signals, List<PortfolioPosition> positions, decimal totalCapital);
    Task<List<TradeSignal>> GenerateBatchSignalsAsync(List<string> codes, List<PortfolioPosition> positions, decimal totalCapital);
}
