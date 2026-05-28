using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Strategy.Services;

/// <summary>
/// 组合管理实现
/// </summary>
public class PortfolioEngineService : IPortfolioEngine
{
    private readonly IPositionSizer _positionSizer;
    private readonly ILogger<PortfolioEngineService> _logger;

    public PortfolioEngineService(IPositionSizer positionSizer, ILogger<PortfolioEngineService> logger)
    {
        _positionSizer = positionSizer;
        _logger = logger;
    }

    public async Task<List<PortfolioPosition>> CalculateTargetPositionsAsync(List<TradeSignal> signals, decimal totalCapital)
    {
        if (signals == null || signals.Count == 0)
            return new List<PortfolioPosition>();

        var positions = new List<PortfolioPosition>();
        var buySignals = signals.Where(s => s.SignalType == "buy" || s.SignalType == "strongbuy").ToList();

        foreach (var signal in buySignals)
        {
            var positionSize = _positionSizer.CalculatePositionSize(signal, totalCapital, PositionSizeMode.EqualWeight);
            var volume = (long)(positionSize / signal.Price / 100) * 100;

            if (volume <= 0) continue;

            positions.Add(new PortfolioPosition
            {
                Code = signal.Code,
                Volume = volume,
                CostPrice = signal.Price,
                CurrentPrice = signal.Price,
                MarketValue = volume * signal.Price,
                Weight = positionSize / totalCapital * 100,
                StopLossPrice = signal.StopLossPrice,
                TakeProfitPrice = signal.TakeProfitPrice
            });
        }

        return positions;
    }

    public async Task<List<TradeSignal>> RebalanceAsync(List<PortfolioPosition> currentPositions, List<PortfolioPosition> targetPositions)
    {
        var signals = new List<TradeSignal>();

        var currentDict = currentPositions.ToDictionary(p => p.Code);
        var targetDict = targetPositions.ToDictionary(p => p.Code);

        foreach (var target in targetPositions)
        {
            if (!currentDict.ContainsKey(target.Code))
            {
                signals.Add(new TradeSignal
                {
                    Code = target.Code,
                    SignalType = SignalType.Buy.ToString().ToLower(),
                    Strength = 80,
                    Price = target.CurrentPrice,
                    Volume = target.Volume,
                    StrategyName = "PortfolioRebalance",
                    Reason = "新建仓位"
                });
            }
            else
            {
                var current = currentDict[target.Code];
                var volumeDiff = target.Volume - current.Volume;

                if (volumeDiff > 100)
                {
                    signals.Add(new TradeSignal
                    {
                        Code = target.Code,
                        SignalType = SignalType.Buy.ToString().ToLower(),
                        Strength = 60,
                        Price = target.CurrentPrice,
                        Volume = volumeDiff,
                        StrategyName = "PortfolioRebalance",
                        Reason = "加仓"
                    });
                }
                else if (volumeDiff < -100)
                {
                    signals.Add(new TradeSignal
                    {
                        Code = target.Code,
                        SignalType = SignalType.Sell.ToString().ToLower(),
                        Strength = 60,
                        Price = target.CurrentPrice,
                        Volume = Math.Abs(volumeDiff),
                        StrategyName = "PortfolioRebalance",
                        Reason = "减仓"
                    });
                }
            }
        }

        foreach (var current in currentPositions)
        {
            if (!targetDict.ContainsKey(current.Code))
            {
                signals.Add(new TradeSignal
                {
                    Code = current.Code,
                    SignalType = SignalType.Sell.ToString().ToLower(),
                    Strength = 80,
                    Price = current.CurrentPrice,
                    Volume = current.Volume,
                    StrategyName = "PortfolioRebalance",
                    Reason = "清仓"
                });
            }
        }

        return signals;
    }

    public async Task<Dictionary<string, decimal>> CalculateWeightsAsync(List<TradeSignal> signals, PositionSizeMode mode)
    {
        if (signals == null || signals.Count == 0)
            return new Dictionary<string, decimal>();

        var weights = new Dictionary<string, decimal>();
        var buySignals = signals.Where(s => s.SignalType == "buy" || s.SignalType == "strongbuy").ToList();

        if (buySignals.Count == 0)
            return weights;

        switch (mode)
        {
            case PositionSizeMode.EqualWeight:
                var equalWeight = 100m / buySignals.Count;
                foreach (var signal in buySignals)
                {
                    weights[signal.Code] = equalWeight;
                }
                break;

            case PositionSizeMode.FixedRatio:
                var totalStrength = buySignals.Sum(s => s.Strength);
                foreach (var signal in buySignals)
                {
                    weights[signal.Code] = signal.Strength / totalStrength * 100;
                }
                break;

            default:
                var defaultWeight = 100m / buySignals.Count;
                foreach (var signal in buySignals)
                {
                    weights[signal.Code] = defaultWeight;
                }
                break;
        }

        return weights;
    }
}
