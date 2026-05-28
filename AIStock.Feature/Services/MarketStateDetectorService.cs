using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Feature.Services;

/// <summary>
/// 市场状态检测实现
/// </summary>
public class MarketStateDetectorService : IMarketStateDetector
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly ILogger<MarketStateDetectorService> _logger;

    public MarketStateDetectorService(
        IDataProviderResolver dataProviderResolver,
        IFeatureCalculator featureCalculator,
        ILogger<MarketStateDetectorService> logger)
    {
        _dataProviderResolver = dataProviderResolver;
        _featureCalculator = featureCalculator;
        _logger = logger;
    }

    public async Task<MarketState> DetectMarketStateAsync(string indexCode = "000001")
    {
        try
        {
            var provider = _dataProviderResolver.GetDefaultProvider();
            var klines = await provider.GetKlinesAsync(indexCode, KlineInterval.Daily, 60);

            if (klines == null || klines.Count < 60)
            {
                _logger.LogWarning("Insufficient data for market state detection");
                return MarketState.Unknown;
            }

            var indicators = _featureCalculator.CalculateAll(indexCode, klines);

            var ma = indicators.MA;
            var volatility = indicators.Volatility ?? 0;

            var currentPrice = klines.Last().Close;
            var ma20 = ma?.MA20 ?? 0;
            var ma60 = ma?.MA60 ?? 0;

            if (currentPrice > ma20 && ma20 > ma60 && volatility < 0.3m)
            {
                return MarketState.Bull;
            }
            else if (currentPrice < ma20 && ma20 < ma60 && volatility < 0.3m)
            {
                return MarketState.Bear;
            }
            else if (volatility > 0.5m)
            {
                return MarketState.Extreme;
            }
            else
            {
                return MarketState.Sideways;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect market state");
            return MarketState.Unknown;
        }
    }

    public async Task<MarketSentiment> GetMarketSentimentAsync()
    {
        try
        {
            var provider = _dataProviderResolver.GetDefaultProvider();
            var stocks = await provider.GetStockListAsync();

            int limitUpCount = 0;
            int limitDownCount = 0;
            int advanceCount = 0;
            int declineCount = 0;
            decimal totalAmount = 0;

            foreach (var stock in stocks.Take(100))
            {
                try
                {
                    var quote = await provider.GetQuoteAsync(stock.Code);
                    if (quote == null) continue;

                    if (quote.ChangePercent >= 9.9m) limitUpCount++;
                    else if (quote.ChangePercent <= -9.9m) limitDownCount++;

                    if (quote.ChangePercent > 0) advanceCount++;
                    else if (quote.ChangePercent < 0) declineCount++;

                    totalAmount += quote.Amount;
                }
                catch
                {
                    continue;
                }
            }

            var advanceDeclineRatio = declineCount > 0 ? (decimal)advanceCount / declineCount : advanceCount;
            var sentimentScore = CalculateSentimentScore(advanceDeclineRatio, limitUpCount, limitDownCount);

            return new MarketSentiment
            {
                AdvanceDeclineRatio = advanceDeclineRatio,
                LimitUpCount = limitUpCount,
                LimitDownCount = limitDownCount,
                TotalAmount = totalAmount / 100000000,
                SentimentScore = sentimentScore
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get market sentiment");
            return new MarketSentiment();
        }
    }

    public async Task<bool> IsExtremeMarketAsync()
    {
        var state = await DetectMarketStateAsync();
        return state == MarketState.Extreme;
    }

    private int CalculateSentimentScore(decimal advanceDeclineRatio, int limitUpCount, int limitDownCount)
    {
        var score = 50;

        if (advanceDeclineRatio > 2m) score += 20;
        else if (advanceDeclineRatio > 1.5m) score += 10;
        else if (advanceDeclineRatio < 0.5m) score -= 20;
        else if (advanceDeclineRatio < 0.67m) score -= 10;

        if (limitUpCount > 50) score += 20;
        else if (limitUpCount > 20) score += 10;
        else if (limitUpCount < 5) score -= 10;

        if (limitDownCount > 50) score -= 20;
        else if (limitDownCount > 20) score -= 10;

        return Math.Max(0, Math.Min(100, score));
    }
}
