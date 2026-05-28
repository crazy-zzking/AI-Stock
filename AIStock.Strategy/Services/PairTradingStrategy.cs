using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;

namespace AIStock.Strategy.Services;

/// <summary>
/// 配对交易策略
/// </summary>
public class PairTradingStrategy : IStrategy
{
    public string Name => "PairTrading";
    public string Description => "配对交易策略 - 利用相关性高的股票价差进行套利";

    private readonly decimal _zScoreThreshold;
    private readonly int _lookbackPeriod;

    public PairTradingStrategy(decimal zScoreThreshold = 2.0m, int lookbackPeriod = 60)
    {
        _zScoreThreshold = zScoreThreshold;
        _lookbackPeriod = lookbackPeriod;
    }

    public async Task<TradeSignal?> GenerateSignalAsync(string code, List<KlineData> klines, TechnicalIndicator indicators)
    {
        return null;
    }

    public async Task<List<TradeSignal>> GenerateSignalsAsync(List<string> codes, DateTime dateTime)
    {
        return new List<TradeSignal>();
    }

    public async Task<TradeSignal?> GeneratePairSignalAsync(string code1, string code2, List<KlineData> klines1, List<KlineData> klines2)
    {
        if (klines1 == null || klines2 == null || klines1.Count < _lookbackPeriod || klines2.Count < _lookbackPeriod)
            return null;

        var prices1 = klines1.Skip(klines1.Count - _lookbackPeriod).Select(k => k.Close).ToList();
        var prices2 = klines2.Skip(klines2.Count - _lookbackPeriod).Select(k => k.Close).ToList();

        var spread = new List<decimal>();
        for (int i = 0; i < prices1.Count; i++)
        {
            if (prices2[i] > 0)
            {
                spread.Add(prices1[i] / prices2[i]);
            }
        }

        if (spread.Count < _lookbackPeriod)
            return null;

        var mean = spread.Average();
        var variance = spread.Select(s => (s - mean) * (s - mean)).Average();
        var stdDev = (decimal)Math.Sqrt((double)variance);
        var currentSpread = spread.Last();
        var zScore = stdDev > 0 ? (currentSpread - mean) / stdDev : 0;

        if (zScore > _zScoreThreshold)
        {
            return new TradeSignal
            {
                Code = code1,
                SignalType = SignalType.Sell,
                Strength = 70,
                Price = klines1.Last().Close,
                StrategyName = Name,
                Reason = $"配对交易做空信号，Z-Score: {zScore:F2}",
                StopLossPrice = klines1.Last().Close * 1.05m,
                TakeProfitPrice = klines1.Last().Close * 0.95m
            };
        }

        if (zScore < -_zScoreThreshold)
        {
            return new TradeSignal
            {
                Code = code1,
                SignalType = SignalType.Buy,
                Strength = 70,
                Price = klines1.Last().Close,
                StrategyName = Name,
                Reason = $"配对交易做多信号，Z-Score: {zScore:F2}",
                StopLossPrice = klines1.Last().Close * 0.95m,
                TakeProfitPrice = klines1.Last().Close * 1.05m
            };
        }

        return null;
    }
}
