using System.Net;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers;

/// <summary>
/// 数据源提供者基类
/// </summary>
public abstract class BaseProvider : IDataProvider
{
    protected readonly ILogger Logger;
    protected readonly HttpClient HttpClient;
    protected readonly IWebProxy? Proxy;

    protected BaseProvider(ILogger logger, HttpClient httpClient, IWebProxy? proxy = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Proxy = proxy;
    }

    /// <summary>
    /// 提供者ID
    /// </summary>
    public abstract string ProviderId { get; }

    /// <summary>
    /// 提供者名称
    /// </summary>
    public abstract string ProviderName { get; }

    /// <summary>
    /// 支持的能力列表
    /// </summary>
    public abstract IEnumerable<DataCapability> Capabilities { get; }

    /// <summary>
    /// 获取实时行情
    /// </summary>
    public abstract Task<QuoteData?> GetQuoteAsync(string code);

    /// <summary>
    /// 批量获取实时行情
    /// </summary>
    public virtual async Task<List<QuoteData>> GetQuotesAsync(IEnumerable<string> codes)
    {
        var results = new List<QuoteData>();
        foreach (var code in codes)
        {
            var quote = await GetQuoteAsync(code);
            if (quote != null)
            {
                results.Add(quote);
            }
        }
        return results;
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public abstract Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100);

    /// <summary>
    /// 获取分时数据
    /// </summary>
    public abstract Task<List<IntradayData>> GetIntradayAsync(string code);

    /// <summary>
    /// 获取历史分时数据
    /// </summary>
    public virtual Task<List<IntradayData>> GetHistoryIntradayAsync(string date, string code)
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support history intraday data");
    }

    /// <summary>
    /// 获取分笔成交数据
    /// </summary>
    public virtual Task<List<TradeData>> GetTradesAsync(string code, int start = 0, int count = 100)
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support trades data");
    }

    /// <summary>
    /// 获取历史分笔成交数据
    /// </summary>
    public virtual Task<List<TradeData>> GetHistoryTradesAsync(string date, string code, int start = 0, int count = 100)
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support history trades data");
    }

    /// <summary>
    /// 获取集合竞价数据
    /// </summary>
    public virtual Task<List<CallAuctionData>> GetCallAuctionAsync(string code)
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support call auction data");
    }

    /// <summary>
    /// 获取股票代码列表
    /// </summary>
    public virtual Task<List<StockCodeInfo>> GetStockCodesAsync(string market)
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support stock codes");
    }

    /// <summary>
    /// 获取股票代码列表（按类型过滤）
    /// </summary>
    public virtual async Task<List<StockCodeInfo>> GetStockCodesAsync(string market, SecurityType securityType)
    {
        var allCodes = await GetStockCodesAsync(market);
        var typeStr = securityType.ToString();
        return allCodes.Where(x => x.SecurityType.Equals(typeStr, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 获取资金流向
    /// </summary>
    public virtual Task<CapitalFlowData?> GetCapitalFlowAsync(string code)
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support capital flow data");
    }

    /// <summary>
    /// 获取股票列表
    /// </summary>
    public virtual Task<List<StockInfo>> GetStockListAsync()
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support stock list");
    }

    /// <summary>
    /// 检查是否为交易日
    /// </summary>
    public virtual Task<bool> IsTradingDayAsync(DateTime date)
    {
        // 默认实现：周一到周五为交易日
        return Task.FromResult(date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday);
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public abstract Task<bool> IsHealthyAsync();

    /// <summary>
    /// 获取账户持仓
    /// </summary>
    public virtual Task<List<AccountPosition>> GetAccountPositionsAsync()
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support account positions");
    }

    /// <summary>
    /// 获取账户资金
    /// </summary>
    public virtual Task<AccountBalance?> GetAccountBalanceAsync()
    {
        throw new NotSupportedException($"Provider {ProviderId} does not support account balance");
    }

    /// <summary>
    /// 发送HTTP请求
    /// </summary>
    protected async Task<string?> SendRequestAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send request to {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// 股票代码转换为市场代码
    /// </summary>
    protected static string GetMarketCode(string code)
    {
        // 60/68/51开头 → 上海
        if (code.StartsWith("60") || code.StartsWith("68") || code.StartsWith("51"))
            return $"1.{code}";

        // 00/30/15/92开头 → 深圳
        if (code.StartsWith("00") || code.StartsWith("30") || code.StartsWith("15") || code.StartsWith("92"))
            return $"0.{code}";

        // 默认返回
        return $"1.{code}";
    }

    /// <summary>
    /// 股票代码转换为带前缀代码
    /// </summary>
    protected static string GetPrefixCode(string code)
    {
        if (code.StartsWith("60") || code.StartsWith("68") || code.StartsWith("51"))
            return $"sh{code}";

        if (code.StartsWith("00") || code.StartsWith("30") || code.StartsWith("15") || code.StartsWith("92"))
            return $"sz{code}";

        return $"sh{code}";
    }
}
