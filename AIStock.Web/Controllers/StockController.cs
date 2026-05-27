using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 股票数据API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StockController : ControllerBase
{
    private readonly IDataProviderResolver _resolver;
    private readonly ILogger<StockController> _logger;

    public StockController(IDataProviderResolver resolver, ILogger<StockController> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取股票列表
    /// </summary>
    [HttpGet("list")]
    public async Task<ActionResult<List<StockInfo>>> GetStockList()
    {
        try
        {
            var provider = _resolver.GetPrimaryProvider(DataCapability.StockUniverse);
            if (provider == null)
                return NotFound("No provider available for stock list");

            var stocks = await provider.GetStockListAsync();
            return Ok(stocks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stock list");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 获取实时行情
    /// </summary>
    [HttpGet("{code}/quote")]
    public async Task<ActionResult<QuoteData>> GetQuote(string code, [FromQuery] string? provider = null)
    {
        try
        {
            IDataProvider? quoteProvider;
            
            if (!string.IsNullOrEmpty(provider))
            {
                // 使用指定的provider
                quoteProvider = _resolver.GetAllProviders()
                    .FirstOrDefault(p => p.ProviderId == provider && p.Capabilities.Contains(DataCapability.Quote));
            }
            else
            {
                quoteProvider = _resolver.GetPrimaryProvider(DataCapability.Quote);
            }
            
            if (quoteProvider == null)
                return NotFound("No provider available for quote");

            var quote = await quoteProvider.GetQuoteAsync(code);
            if (quote == null)
                return NotFound($"Quote not found for {code}");

            return Ok(quote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quote for {Code}", code);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 批量获取实时行情
    /// </summary>
    [HttpPost("quotes")]
    public async Task<ActionResult<List<QuoteData>>> GetQuotes([FromBody] List<string> codes)
    {
        try
        {
            var provider = _resolver.GetPrimaryProvider(DataCapability.Quote);
            if (provider == null)
                return NotFound("No provider available for quote");

            var quotes = await provider.GetQuotesAsync(codes);
            return Ok(quotes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quotes");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    [HttpGet("{code}/kline")]
    public async Task<ActionResult<List<KlineData>>> GetKlines(
        string code,
        [FromQuery] KlineInterval interval = KlineInterval.Daily,
        [FromQuery] int count = 100,
        [FromQuery] string? provider = null)
    {
        try
        {
            IDataProvider? klineProvider;
            
            if (!string.IsNullOrEmpty(provider))
            {
                // 使用指定的provider
                klineProvider = _resolver.GetAllProviders()
                    .FirstOrDefault(p => p.ProviderId == provider && p.Capabilities.Contains(DataCapability.Kline));
            }
            else
            {
                klineProvider = _resolver.GetPrimaryProvider(DataCapability.Kline);
            }
            
            if (klineProvider == null)
                return NotFound("No provider available for kline");

            var klines = await klineProvider.GetKlinesAsync(code, interval, count);
            return Ok(klines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get klines for {Code}", code);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 获取分时数据
    /// </summary>
    [HttpGet("{code}/intraday")]
    public async Task<ActionResult<List<IntradayData>>> GetIntraday(string code)
    {
        try
        {
            var provider = _resolver.GetPrimaryProvider(DataCapability.Intraday);
            if (provider == null)
                return NotFound("No provider available for intraday");

            var intraday = await provider.GetIntradayAsync(code);
            return Ok(intraday);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get intraday for {Code}", code);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 获取资金流向
    /// </summary>
    [HttpGet("{code}/capital-flow")]
    public async Task<ActionResult<CapitalFlowData>> GetCapitalFlow(string code)
    {
        try
        {
            var provider = _resolver.GetPrimaryProvider(DataCapability.CapitalFlow);
            if (provider == null)
                return NotFound("No provider available for capital flow");

            var flow = await provider.GetCapitalFlowAsync(code);
            if (flow == null)
                return NotFound($"Capital flow not found for {code}");

            return Ok(flow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get capital flow for {Code}", code);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 检查交易日
    /// </summary>
    [HttpGet("trading-day")]
    public async Task<ActionResult<bool>> IsTradingDay([FromQuery] DateTime? date)
    {
        try
        {
            var targetDate = date ?? DateTime.Today;
            var provider = _resolver.GetPrimaryProvider(DataCapability.TradingCalendar);
            if (provider == null)
                return NotFound("No provider available for trading calendar");

            var isTradingDay = await provider.IsTradingDayAsync(targetDate);
            return Ok(isTradingDay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check trading day");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 获取数据源状态
    /// </summary>
    [HttpGet("providers/status")]
    public async Task<ActionResult<List<ProviderStatus>>> GetProviderStatus()
    {
        try
        {
            var providers = _resolver.GetAllProviders();
            var statusList = new List<ProviderStatus>();

            foreach (var provider in providers)
            {
                var isHealthy = await provider.IsHealthyAsync();
                statusList.Add(new ProviderStatus
                {
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.ProviderName,
                    IsHealthy = isHealthy,
                    Capabilities = provider.Capabilities.ToList()
                });
            }

            return Ok(statusList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get provider status");
            return StatusCode(500, "Internal server error");
        }
    }
}

/// <summary>
/// 数据源状态
/// </summary>
public class ProviderStatus
{
    /// <summary>
    /// 提供者ID
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// 提供者名称
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// 支持的能力
    /// </summary>
    public List<DataCapability> Capabilities { get; set; } = new();
}
