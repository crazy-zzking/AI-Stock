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
    private readonly IEnumerable<IStrategy> _strategies;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<SignalGeneratorService> _logger;

    public SignalGeneratorService(
        IAlphaEngine alphaEngine,
        IRiskEngine riskEngine,
        IEnumerable<IStrategy> strategies,
        IFeatureCalculator featureCalculator,
        IDataProviderResolver dataProviderResolver,
        ILogger<SignalGeneratorService> logger)
    {
        _alphaEngine = alphaEngine;
        _riskEngine = riskEngine;
        _strategies = strategies;
        _featureCalculator = featureCalculator;
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    public async Task<TradeSignal> GenerateFinalSignalAsync(string code, List<TradeSignal> signals, List<PortfolioPosition> positions, decimal totalCapital)
    {
        var compositeSignal = await _alphaEngine.GenerateCompositeSignalAsync(code, signals);

        if (compositeSignal.SignalType == SignalType.Hold)
        {
            return compositeSignal;
        }

        var riskCheck = await _riskEngine.CheckRiskAsync(compositeSignal, positions, totalCapital);

        if (!riskCheck.Passed)
        {
            _logger.LogWarning("Risk check failed for {Code}: {Suggestion}", code, riskCheck.Suggestion);

            if (riskCheck.RiskLevel == RiskLevel.Critical)
            {
                compositeSignal.SignalType = SignalType.Hold;
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
                // 获取K线数据
                var provider = _dataProviderResolver.GetDefaultProvider();
                if (provider == null)
                {
                    _logger.LogWarning("No data provider available for {Code}", code);
                    continue;
                }

                var klines = await provider.GetKlinesAsync(code, KlineInterval.Daily, 100);
                if (klines == null || klines.Count < 60)
                {
                    _logger.LogWarning("Insufficient kline data for {Code}", code);
                    continue;
                }

                // 计算技术指标
                var indicators = _featureCalculator.CalculateAll(code, klines);

                // 调用各策略生成原始信号
                var rawSignals = new List<TradeSignal>();
                foreach (var strategy in _strategies)
                {
                    try
                    {
                        var signal = await strategy.GenerateSignalAsync(code, klines, indicators);
                        if (signal != null)
                        {
                            rawSignals.Add(signal);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Strategy {Strategy} failed for {Code}", strategy.Name, code);
                    }
                }

                if (rawSignals.Count == 0)
                {
                    _logger.LogDebug("No signals generated for {Code}", code);
                    continue;
                }

                // 融合信号
                var compositeSignal = await _alphaEngine.GenerateCompositeSignalAsync(code, rawSignals);

                if (compositeSignal.SignalType == SignalType.Hold)
                {
                    continue;
                }

                // 风控检查
                var riskCheck = await _riskEngine.CheckRiskAsync(compositeSignal, positions, totalCapital);
                if (!riskCheck.Passed && riskCheck.RiskLevel == RiskLevel.Critical)
                {
                    continue;
                }

                if (!riskCheck.Passed)
                {
                    compositeSignal.Strength = Math.Max(0, compositeSignal.Strength - 20);
                    compositeSignal.Reason += $" (风控警告: {riskCheck.Suggestion})";
                }

                results.Add(compositeSignal);
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
