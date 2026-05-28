using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 特征工程API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FeatureController : ControllerBase
{
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IFeatureStore _featureStore;
    private readonly IMarketStateDetector _marketStateDetector;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<FeatureController> _logger;

    public FeatureController(
        IFeatureCalculator featureCalculator,
        IFeatureStore featureStore,
        IMarketStateDetector marketStateDetector,
        IDataProviderResolver dataProviderResolver,
        ILogger<FeatureController> logger)
    {
        _featureCalculator = featureCalculator;
        _featureStore = featureStore;
        _marketStateDetector = marketStateDetector;
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    /// <summary>
    /// 计算股票技术指标
    /// </summary>
    [HttpGet("{code}/indicators")]
    public async Task<ActionResult<TechnicalIndicator>> GetIndicators(string code, [FromQuery] int count = 100)
    {
        try
        {
            var provider = _dataProviderResolver.GetDefaultProvider();
            var klines = await provider.GetKlinesAsync(code, KlineInterval.Daily, count);

            if (klines == null || klines.Count == 0)
            {
                return NotFound($"No kline data found for {code}");
            }

            var intradayData = await provider.GetIntradayAsync(code);
            var indicators = _featureCalculator.CalculateAll(code, klines, intradayData);

            await _featureStore.SaveFeaturesAsync(code, DateTime.UtcNow, indicators);

            return Ok(indicators);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate indicators for {Code}", code);
            return StatusCode(500, "Failed to calculate indicators");
        }
    }

    /// <summary>
    /// 获取最新特征
    /// </summary>
    [HttpGet("{code}/latest")]
    public async Task<ActionResult<TechnicalIndicator>> GetLatestFeatures(string code)
    {
        var features = await _featureStore.GetLatestFeaturesAsync(code);
        if (features == null)
        {
            return NotFound($"No features found for {code}");
        }
        return Ok(features);
    }

    /// <summary>
    /// 获取历史特征
    /// </summary>
    [HttpGet("{code}/history")]
    public async Task<ActionResult<List<TechnicalIndicator>>> GetHistoricalFeatures(
        string code,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var features = await _featureStore.GetHistoricalFeaturesAsync(code, startTime, endTime);
        return Ok(features);
    }

    /// <summary>
    /// 检测市场状态
    /// </summary>
    [HttpGet("market/state")]
    public async Task<ActionResult<MarketState>> GetMarketState([FromQuery] string indexCode = "000001")
    {
        var state = await _marketStateDetector.DetectMarketStateAsync(indexCode);
        return Ok(state);
    }

    /// <summary>
    /// 获取市场情绪
    /// </summary>
    [HttpGet("market/sentiment")]
    public async Task<ActionResult<MarketSentiment>> GetMarketSentiment()
    {
        var sentiment = await _marketStateDetector.GetMarketSentimentAsync();
        return Ok(sentiment);
    }

    /// <summary>
    /// 检测极端行情
    /// </summary>
    [HttpGet("market/extreme")]
    public async Task<ActionResult<bool>> IsExtremeMarket()
    {
        var isExtreme = await _marketStateDetector.IsExtremeMarketAsync();
        return Ok(isExtreme);
    }
}
