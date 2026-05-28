using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Risk.Services;

/// <summary>
/// 止损系统实现
/// </summary>
public class StopLossSystemService : IStopLossSystem
{
    private readonly RiskConfig _config;
    private readonly ILogger<StopLossSystemService> _logger;

    public StopLossSystemService(ILogger<StopLossSystemService> logger) : this(logger, new RiskConfig()) { }

    public StopLossSystemService(ILogger<StopLossSystemService> logger, RiskConfig config)
    {
        _config = config;
        _logger = logger;
    }

    public decimal CalculateStopLoss(TradeSignal signal, List<KlineData> klines, StopLossMode mode)
    {
        switch (mode)
        {
            case StopLossMode.ATR:
                var atr = CalculateATR(klines);
                return CalculateATRStopLoss(signal.Price, atr, _config.AtrMultiplier);
            case StopLossMode.Fixed:
                return CalculateFixedStopLoss(signal.Price, _config.DefaultFixedStopPercent);
            case StopLossMode.Trailing:
                var highestPrice = klines.Any() ? klines.Max(k => k.High) : signal.Price;
                return CalculateTrailingStop(highestPrice, _config.DefaultTrailingPercent);
            default:
                return signal.Price * _config.DefaultStopLossMultiplier;
        }
    }

    public decimal CalculateTakeProfit(TradeSignal signal, List<KlineData> klines, StopLossMode mode)
    {
        switch (mode)
        {
            case StopLossMode.ATR:
                var atr = CalculateATR(klines);
                return signal.Price + atr * _config.AtrMultiplier;
            case StopLossMode.Fixed:
                return signal.Price * _config.DefaultTakeProfitMultiplier;
            case StopLossMode.Trailing:
                return signal.Price * _config.DefaultTrailingTakeProfitMultiplier;
            default:
                return signal.Price * _config.DefaultTakeProfitMultiplier;
        }
    }

    public decimal CalculateATRStopLoss(decimal entryPrice, decimal atr, decimal multiplier = 2)
    {
        return entryPrice - atr * multiplier;
    }

    public decimal CalculateFixedStopLoss(decimal entryPrice, decimal stopPercent)
    {
        return entryPrice * (1 - stopPercent / 100);
    }

    public decimal CalculateTrailingStop(decimal highestPrice, decimal trailingPercent)
    {
        return highestPrice * (1 - trailingPercent / 100);
    }

    private decimal CalculateATR(List<KlineData> klines, int period = 14)
    {
        if (klines == null || klines.Count < period + 1)
            return 0;

        var trueRanges = new List<decimal>();
        for (int i = 1; i < klines.Count; i++)
        {
            var tr = Math.Max(
                klines[i].High - klines[i].Low,
                Math.Max(
                    Math.Abs(klines[i].High - klines[i - 1].Close),
                    Math.Abs(klines[i].Low - klines[i - 1].Close)
                )
            );
            trueRanges.Add(tr);
        }

        if (trueRanges.Count < period)
            return 0;

        return trueRanges.Skip(trueRanges.Count - period).Average();
    }
}
