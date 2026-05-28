using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 特征计算器接口
/// </summary>
public interface IFeatureCalculator
{
    /// <summary>
    /// 计算MA指标
    /// </summary>
    MAIndicator CalculateMA(List<KlineData> klines);

    /// <summary>
    /// 计算MACD指标
    /// </summary>
    MACDIndicator CalculateMACD(List<KlineData> klines, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9);

    /// <summary>
    /// 计算RSI指标
    /// </summary>
    RSIIndicator CalculateRSI(List<KlineData> klines);

    /// <summary>
    /// 计算VWAP指标
    /// </summary>
    decimal CalculateVWAP(List<IntradayData> intradayData);

    /// <summary>
    /// 计算波动率
    /// </summary>
    decimal CalculateVolatility(List<KlineData> klines, int period = 20);

    /// <summary>
    /// 计算ATR指标
    /// </summary>
    decimal CalculateATR(List<KlineData> klines, int period = 14);

    /// <summary>
    /// 计算所有技术指标
    /// </summary>
    TechnicalIndicator CalculateAll(string code, List<KlineData> klines, List<IntradayData>? intradayData = null);
}
