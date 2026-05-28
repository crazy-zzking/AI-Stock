using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Strategy.Services;

/// <summary>
/// Alpha引擎实现
/// </summary>
public class AlphaEngineService : IAlphaEngine
{
    private readonly ILogger<AlphaEngineService> _logger;

    public AlphaEngineService(ILogger<AlphaEngineService> logger)
    {
        _logger = logger;
    }

    public async Task<TradeSignal> GenerateCompositeSignalAsync(string code, List<TradeSignal> signals)
    {
        if (signals == null || signals.Count == 0)
        {
            return new TradeSignal
            {
                Code = code,
                SignalType = SignalType.Hold.ToString().ToLower(),
                Strength = 0,
                StrategyName = "AlphaEngine",
                Reason = "无有效信号"
            };
        }

        var buySignals = signals.Where(s => s.SignalType == "buy" || s.SignalType == "strongbuy").ToList();
        var sellSignals = signals.Where(s => s.SignalType == "sell" || s.SignalType == "strongsell").ToList();

        var buyStrength = buySignals.Sum(s => s.Strength);
        var sellStrength = sellSignals.Sum(s => s.Strength);

        var signal = new TradeSignal
        {
            Code = code,
            StrategyName = "AlphaEngine",
            SignalTime = DateTime.UtcNow
        };

        if (buyStrength > sellStrength && buySignals.Count > 0)
        {
            signal.SignalType = buyStrength > 150 ? SignalType.StrongBuy.ToString().ToLower() : SignalType.Buy.ToString().ToLower();
            signal.Strength = Math.Min(100, buyStrength / buySignals.Count);
            signal.Price = buySignals.First().Price;
            signal.Reason = $"多头信号融合: {buySignals.Count}个买入信号，总强度: {buyStrength}";
            signal.StopLossPrice = buySignals.Min(s => s.StopLossPrice ?? signal.Price * 0.95m);
            signal.TakeProfitPrice = buySignals.Max(s => s.TakeProfitPrice ?? signal.Price * 1.1m);
        }
        else if (sellStrength > buyStrength && sellSignals.Count > 0)
        {
            signal.SignalType = sellStrength > 150 ? SignalType.StrongSell.ToString().ToLower() : SignalType.Sell.ToString().ToLower();
            signal.Strength = Math.Min(100, sellStrength / sellSignals.Count);
            signal.Price = sellSignals.First().Price;
            signal.Reason = $"空头信号融合: {sellSignals.Count}个卖出信号，总强度: {sellStrength}";
            signal.StopLossPrice = sellSignals.Max(s => s.StopLossPrice ?? signal.Price * 1.05m);
            signal.TakeProfitPrice = sellSignals.Min(s => s.TakeProfitPrice ?? signal.Price * 0.9m);
        }
        else
        {
            signal.SignalType = SignalType.Hold.ToString().ToLower();
            signal.Strength = 0;
            signal.Price = signals.First().Price;
            signal.Reason = "多空信号平衡，建议观望";
        }

        return signal;
    }

    public async Task<TradeSignal> MergeSignalsAsync(List<TradeSignal> signals)
    {
        if (signals == null || signals.Count == 0)
            return new TradeSignal { SignalType = SignalType.Hold.ToString().ToLower() };

        var grouped = signals.GroupBy(s => s.Code);
        var mergedSignals = new List<TradeSignal>();

        foreach (var group in grouped)
        {
            var compositeSignal = await GenerateCompositeSignalAsync(group.Key, group.ToList());
            mergedSignals.Add(compositeSignal);
        }

        return mergedSignals.OrderByDescending(s => s.Strength).First();
    }

    public async Task<decimal> CalculateConfidenceAsync(TradeSignal signal)
    {
        if (signal == null) return 0;

        var confidence = signal.Strength;

        if (signal.StrategyName == "AlphaEngine")
        {
            confidence = Math.Min(100, confidence + 10);
        }

        if (signal.StopLossPrice.HasValue && signal.TakeProfitPrice.HasValue)
        {
            var riskRewardRatio = Math.Abs(signal.TakeProfitPrice.Value - signal.Price) / Math.Abs(signal.Price - signal.StopLossPrice.Value);
            if (riskRewardRatio > 2)
            {
                confidence = Math.Min(100, confidence + 10);
            }
        }

        return confidence;
    }
}
