using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;

namespace AIStock.Strategy.Services;

/// <summary>
/// 网格交易策略
/// </summary>
public class GridTradingStrategy : IStrategy
{
    public string Name => "GridTrading";
    public string Description => "网格交易策略 - 在价格网格中自动买卖";

    private readonly decimal _gridSize;
    private readonly int _gridCount;

    public GridTradingStrategy(decimal gridSize = 0.02m, int gridCount = 10)
    {
        _gridSize = gridSize;
        _gridCount = gridCount;
    }

    public async Task<TradeSignal?> GenerateSignalAsync(string code, List<KlineData> klines, TechnicalIndicator indicators)
    {
        if (klines == null || klines.Count < 20)
            return null;

        var currentPrice = klines.Last().Close;
        var ma20 = indicators.MA?.MA20 ?? currentPrice;

        var gridLevel = Math.Floor((currentPrice - ma20 * (1 - _gridSize * _gridCount / 2)) / (ma20 * _gridSize));

        var prevPrice = klines[klines.Count - 2].Close;
        var prevGridLevel = Math.Floor((prevPrice - ma20 * (1 - _gridSize * _gridCount / 2)) / (ma20 * _gridSize));

        if (gridLevel > prevGridLevel && currentPrice < ma20)
        {
            return new TradeSignal
            {
                Code = code,
                SignalType = SignalType.Buy.ToString().ToLower(),
                Strength = 60,
                Price = currentPrice,
                StrategyName = Name,
                Reason = $"网格买入信号，当前网格层级: {gridLevel}",
                StopLossPrice = currentPrice * (1 - _gridSize * 2),
                TakeProfitPrice = currentPrice * (1 + _gridSize)
            };
        }

        if (gridLevel < prevGridLevel && currentPrice > ma20)
        {
            return new TradeSignal
            {
                Code = code,
                SignalType = SignalType.Sell.ToString().ToLower(),
                Strength = 60,
                Price = currentPrice,
                StrategyName = Name,
                Reason = $"网格卖出信号，当前网格层级: {gridLevel}",
                StopLossPrice = currentPrice * (1 + _gridSize * 2),
                TakeProfitPrice = currentPrice * (1 - _gridSize)
            };
        }

        return null;
    }

    public async Task<List<TradeSignal>> GenerateSignalsAsync(List<string> codes, DateTime dateTime)
    {
        return new List<TradeSignal>();
    }
}
