using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;

namespace AIStock.Strategy.Services;

/// <summary>
/// 均线突破策略
/// </summary>
public class MABreakoutStrategy : IStrategy
{
    public string Name => "MABreakout";
    public string Description => "均线突破策略 - 当价格突破均线时产生信号";

    public async Task<TradeSignal?> GenerateSignalAsync(string code, List<KlineData> klines, TechnicalIndicator indicators)
    {
        if (klines == null || klines.Count < 60 || indicators.MA == null)
            return null;

        var currentPrice = klines.Last().Close;
        var ma5 = indicators.MA.MA5 ?? 0;
        var ma10 = indicators.MA.MA10 ?? 0;
        var ma20 = indicators.MA.MA20 ?? 0;
        var ma60 = indicators.MA.MA60 ?? 0;

        var prevPrice = klines[klines.Count - 2].Close;
        var prevMa5 = CalculateMA(klines.Take(klines.Count - 1).ToList(), 5);
        var prevMa10 = CalculateMA(klines.Take(klines.Count - 1).ToList(), 10);

        if (currentPrice > ma5 && prevPrice <= prevMa5 && ma5 > ma10 && ma10 > ma20)
        {
            return new TradeSignal
            {
                Code = code,
                SignalType = SignalType.Buy.ToString().ToLower(),
                Strength = 80,
                Price = currentPrice,
                StrategyName = Name,
                Reason = "价格突破MA5，均线多头排列",
                StopLossPrice = currentPrice * 0.95m,
                TakeProfitPrice = currentPrice * 1.1m
            };
        }

        if (currentPrice < ma5 && prevPrice >= prevMa5 && ma5 < ma10 && ma10 < ma20)
        {
            return new TradeSignal
            {
                Code = code,
                SignalType = SignalType.Sell.ToString().ToLower(),
                Strength = 80,
                Price = currentPrice,
                StrategyName = Name,
                Reason = "价格跌破MA5，均线空头排列",
                StopLossPrice = currentPrice * 1.05m,
                TakeProfitPrice = currentPrice * 0.9m
            };
        }

        return null;
    }

    public async Task<List<TradeSignal>> GenerateSignalsAsync(List<string> codes, DateTime dateTime)
    {
        return new List<TradeSignal>();
    }

    private decimal CalculateMA(List<KlineData> klines, int period)
    {
        if (klines.Count < period)
            return 0;
        return klines.Skip(klines.Count - period).Average(k => k.Close);
    }
}
