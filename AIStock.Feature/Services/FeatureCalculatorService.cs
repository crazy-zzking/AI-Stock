using AIStock.Core.Interfaces;
using AIStock.Core.Models;

namespace AIStock.Feature.Services;

/// <summary>
/// 特征计算器实现
/// </summary>
public class FeatureCalculatorService : IFeatureCalculator
{
    public MAIndicator CalculateMA(List<KlineData> klines)
    {
        var result = new MAIndicator();

        if (klines == null || klines.Count < 5)
            return result;

        var closes = klines.Select(k => k.Close).ToList();

        // BBI 用均线（短中周期）
        if (closes.Count >= 3) result.MA3 = CalculateSMA(closes, 3);
        if (closes.Count >= 6) result.MA6 = CalculateSMA(closes, 6);
        if (closes.Count >= 12) result.MA12 = CalculateSMA(closes, 12);
        if (closes.Count >= 24) result.MA24 = CalculateSMA(closes, 24);

        result.MA5 = CalculateSMA(closes, 5);
        if (closes.Count >= 10) result.MA10 = CalculateSMA(closes, 10);
        if (closes.Count >= 20) result.MA20 = CalculateSMA(closes, 20);
        if (closes.Count >= 60) result.MA60 = CalculateSMA(closes, 60);
        if (closes.Count >= 120) result.MA120 = CalculateSMA(closes, 120);
        if (closes.Count >= 250) result.MA250 = CalculateSMA(closes, 250);

        return result;
    }

    public decimal CalculateBBI(List<KlineData> klines)
    {
        if (klines == null || klines.Count < 24) return 0;
        var closes = klines.Select(k => k.Close).ToList();
        var bbi = (CalculateSMA(closes, 3) + CalculateSMA(closes, 6)
                   + CalculateSMA(closes, 12) + CalculateSMA(closes, 24)) / 4m;
        return Math.Round(bbi, 4);
    }

    public MACDIndicator CalculateMACD(List<KlineData> klines, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        if (klines == null || klines.Count < slowPeriod + signalPeriod)
            return new MACDIndicator();

        var closes = klines.Select(k => k.Close).ToList();
        var fastEMA = CalculateEMA(closes, fastPeriod);
        var slowEMA = CalculateEMA(closes, slowPeriod);

        var difList = new List<decimal>();
        for (int i = 0; i < closes.Count; i++)
        {
            difList.Add(fastEMA[i] - slowEMA[i]);
        }

        var deaList = CalculateEMA(difList, signalPeriod);
        var macdValue = difList[difList.Count - 1] - deaList[deaList.Count - 1];

        return new MACDIndicator
        {
            DIF = difList[difList.Count - 1],
            DEA = deaList[deaList.Count - 1],
            MACD = macdValue * 2
        };
    }

    public RSIIndicator CalculateRSI(List<KlineData> klines)
    {
        if (klines == null || klines.Count < 24)
            return new RSIIndicator();

        var closes = klines.Select(k => k.Close).ToList();

        return new RSIIndicator
        {
            RSI6 = CalculateRSIValue(closes, 6),
            RSI12 = CalculateRSIValue(closes, 12),
            RSI24 = CalculateRSIValue(closes, 24)
        };
    }

    public decimal CalculateVWAP(List<IntradayData> intradayData)
    {
        if (intradayData == null || intradayData.Count == 0)
            return 0;

        decimal totalAmount = 0;
        long totalVolume = 0;

        foreach (var data in intradayData)
        {
            totalAmount += data.Price * data.Volume;
            totalVolume += data.Volume;
        }

        return totalVolume > 0 ? totalAmount / totalVolume : 0;
    }

    public decimal CalculateVolatility(List<KlineData> klines, int period = 20)
    {
        if (klines == null || klines.Count < period + 1)
            return 0;

        var returns = new List<decimal>();
        for (int i = 1; i < klines.Count; i++)
        {
            if (klines[i - 1].Close > 0)
            {
                returns.Add((klines[i].Close - klines[i - 1].Close) / klines[i - 1].Close);
            }
        }

        if (returns.Count < period)
            return 0;

        var recentReturns = returns.Skip(returns.Count - period).ToList();
        var mean = recentReturns.Average();
        var variance = recentReturns.Select(r => (r - mean) * (r - mean)).Average();

        return (decimal)Math.Sqrt((double)variance) * (decimal)Math.Sqrt(252);
    }

    public decimal CalculateATR(List<KlineData> klines, int period = 14)
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

    public TechnicalIndicator CalculateAll(string code, List<KlineData> klines, List<IntradayData>? intradayData = null)
    {
        var indicator = new TechnicalIndicator
        {
            Code = code,
            DateTime = DateTime.Now
        };

        if (klines != null && klines.Count > 0)
        {
            indicator.DateTime = klines.Last().DateTime;
            indicator.MA = CalculateMA(klines);
            indicator.MACD = CalculateMACD(klines);
            indicator.RSI = CalculateRSI(klines);
            indicator.Volatility = CalculateVolatility(klines);
            indicator.ATR = CalculateATR(klines);
            indicator.BBI = CalculateBBI(klines);
        }

        if (intradayData != null && intradayData.Count > 0)
        {
            indicator.VWAP = CalculateVWAP(intradayData);
        }

        return indicator;
    }

    private decimal CalculateSMA(List<decimal> values, int period)
    {
        if (values.Count < period)
            return 0;

        return values.Skip(values.Count - period).Average();
    }

    private List<decimal> CalculateEMA(List<decimal> values, int period)
    {
        var result = new List<decimal>();
        if (values.Count == 0)
            return result;

        var multiplier = 2m / (period + 1);
        result.Add(values[0]);

        for (int i = 1; i < values.Count; i++)
        {
            var ema = (values[i] - result[i - 1]) * multiplier + result[i - 1];
            result.Add(ema);
        }

        return result;
    }

    private decimal CalculateRSIValue(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1)
            return 0;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0)
            {
                gains.Add(change);
                losses.Add(0);
            }
            else
            {
                gains.Add(0);
                losses.Add(Math.Abs(change));
            }
        }

        if (gains.Count < period)
            return 0;

        var avgGain = gains.Skip(gains.Count - period).Average();
        var avgLoss = losses.Skip(losses.Count - period).Average();

        if (avgLoss == 0)
            return 100;

        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }
}
