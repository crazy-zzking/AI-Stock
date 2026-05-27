using System.Net;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Data.Providers;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers.Tdx;

/// <summary>
/// 通达信数据提供者
/// 注意：通达信使用TCP Socket通信，这里简化为HTTP接口实现
/// 实际生产环境需要使用通达信SDK或TCP协议
/// </summary>
public class TdxProvider : BaseProvider
{
    public TdxProvider(
        ILogger<TdxProvider> logger,
        HttpClient httpClient,
        IWebProxy? proxy = null)
        : base(logger, httpClient, proxy)
    {
    }

    public override string ProviderId => "tdx";
    public override string ProviderName => "通达信";

    public override IEnumerable<DataCapability> Capabilities => new[]
    {
        DataCapability.Quote,
        DataCapability.Kline,
        DataCapability.Intraday,
        DataCapability.StockUniverse
    };

    /// <summary>
    /// 获取实时行情
    /// </summary>
    public override Task<QuoteData?> GetQuoteAsync(string code)
    {
        // 通达信需要TCP连接，这里返回不支持
        Logger.LogWarning("TDX quote requires TCP connection, not supported in HTTP mode");
        return Task.FromResult<QuoteData?>(null);
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public override Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100)
    {
        // 通达信需要TCP连接，这里返回空列表
        Logger.LogWarning("TDX kline requires TCP connection, not supported in HTTP mode");
        return Task.FromResult(new List<KlineData>());
    }

    /// <summary>
    /// 获取分时数据
    /// </summary>
    public override Task<List<IntradayData>> GetIntradayAsync(string code)
    {
        // 通达信需要TCP连接，这里返回空列表
        Logger.LogWarning("TDX intraday requires TCP connection, not supported in HTTP mode");
        return Task.FromResult(new List<IntradayData>());
    }

    /// <summary>
    /// 获取股票列表
    /// </summary>
    public override Task<List<StockInfo>> GetStockListAsync()
    {
        // 通达信需要TCP连接，这里返回空列表
        Logger.LogWarning("TDX stock list requires TCP connection, not supported in HTTP mode");
        return Task.FromResult(new List<StockInfo>());
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public override Task<bool> IsHealthyAsync()
    {
        // 通达信需要TCP连接，这里返回false
        return Task.FromResult(false);
    }
}
