using System.Collections.Concurrent;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers.Tdx;

/// <summary>
/// 通达信数据提供者
/// </summary>
public class TdxProvider : IDataProvider, IDisposable
{
    private readonly ILogger<TdxProvider> _logger;
    private readonly string _host;
    private readonly int _port;
    private TdxClient? _client;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, StockInfo> _stockCache = new();
    private DateTime _lastConnectTime = DateTime.MinValue;
    private bool _disposed;

    public string ProviderId => "tdx";
    public string ProviderName => "通达信";

    public IEnumerable<DataCapability> Capabilities => new[]
    {
        DataCapability.Quote,
        DataCapability.Kline,
        DataCapability.Intraday,
        DataCapability.HistoryIntraday,
        DataCapability.Trades,
        DataCapability.HistoryTrades,
        DataCapability.CallAuction,
        DataCapability.StockUniverse
    };

    public TdxProvider(ILogger<TdxProvider> logger, string host = "119.147.212.81", int port = 7709)
    {
        _logger = logger;
        _host = host;
        _port = port;
    }

    private async Task<TdxClient> GetClientAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null && (DateTime.Now - _lastConnectTime).TotalMinutes < 30)
            return _client;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_client != null)
                await _client.DisposeAsync();
            _client = new TdxClient(_host, _port);
            await _client.ConnectAsync(cancellationToken);
            _lastConnectTime = DateTime.Now;
            _logger.LogInformation("Connected to TDX server {Host}:{Port}", _host, _port);
            return _client;
        }
        catch
        {
            _lock.Release();
            throw;
        }
    }

    public async Task<QuoteData?> GetQuoteAsync(string code)
    {
        try
        {
            var client = await GetClientAsync();
            var quotes = await client.GetQuotesAsync(new[] { code });
            var quote = quotes.FirstOrDefault();

            if (quote == null)
                return null;

            return new QuoteData
            {
                Code = code,
                Name = "",  // 通达信报价接口不返回名称
                Price = (decimal)quote.Last,
                PreClose = (decimal)quote.PreviousClose,
                Open = (decimal)quote.Open,
                High = (decimal)quote.High,
                Low = (decimal)quote.Low,
                Volume = quote.TotalHand * 100,
                Amount = (decimal)quote.Amount,
                ChangePercent = quote.PreviousClose > 0 ? (decimal)((quote.Last - quote.PreviousClose) / quote.PreviousClose * 100) : 0,
                ChangeAmount = (decimal)(quote.Last - quote.PreviousClose),
                InnerVolume = quote.InsideDish,
                OuterVolume = quote.OuterDisc,
                Timestamp = DateTime.UtcNow,
                Source = ProviderId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quote for {Code}", code);
            return null;
        }
    }

    public async Task<List<QuoteData>> GetQuotesAsync(IEnumerable<string> codes)
    {
        try
        {
            var client = await GetClientAsync();
            var quotes = await client.GetQuotesAsync(codes);

            return quotes.Select(quote => new QuoteData
            {
                Code = quote.Code,
                Name = "",  // 通达信报价接口不返回名称
                Price = (decimal)quote.Last,
                PreClose = (decimal)quote.PreviousClose,
                Open = (decimal)quote.Open,
                High = (decimal)quote.High,
                Low = (decimal)quote.Low,
                Volume = quote.TotalHand * 100,
                Amount = (decimal)quote.Amount,
                ChangePercent = quote.PreviousClose > 0 ? (decimal)((quote.Last - quote.PreviousClose) / quote.PreviousClose * 100) : 0,
                ChangeAmount = (decimal)(quote.Last - quote.PreviousClose),
                InnerVolume = quote.InsideDish,
                OuterVolume = quote.OuterDisc,
                Timestamp = DateTime.UtcNow,
                Source = ProviderId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quotes");
            return new List<QuoteData>();
        }
    }

    public async Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100)
    {
        try
        {
            var client = await GetClientAsync();
            var tdxType = interval switch
            {
                KlineInterval.Min1 => TdxKlineType.Minute,
                KlineInterval.Min5 => TdxKlineType.Minute5,
                KlineInterval.Min15 => TdxKlineType.Minute15,
                KlineInterval.Min30 => TdxKlineType.Minute30,
                KlineInterval.Min60 => TdxKlineType.Minute60,
                KlineInterval.Daily => TdxKlineType.Day,
                KlineInterval.Weekly => TdxKlineType.Week,
                KlineInterval.Monthly => TdxKlineType.Month,
                _ => TdxKlineType.Day
            };

            var result = new List<KlineData>();
            var start = 0;
            var remaining = count;

            while (remaining > 0)
            {
                var batchSize = (ushort)Math.Min(remaining, 800);
                var response = await client.GetKlinesAsync(code, tdxType, (ushort)start, batchSize);

                if (response.Items.Count == 0)
                    break;

                result.AddRange(response.Items.Select(item => new KlineData
                {
                    Code = code,
                    DateTime = item.Time,
                    Open = (decimal)item.Open,
                    Close = (decimal)item.Close,
                    High = (decimal)item.High,
                    Low = (decimal)item.Low,
                    Volume = item.Volume,
                    Amount = (decimal)item.Amount,
                    Source = ProviderId
                }));

                start += response.Items.Count;
                remaining -= response.Items.Count;

                if (response.Items.Count < batchSize)
                    break;
            }

            return result.OrderBy(x => x.DateTime).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get klines for {Code}", code);
            return new List<KlineData>();
        }
    }

    public async Task<List<IntradayData>> GetIntradayAsync(string code)
    {
        try
        {
            var client = await GetClientAsync();
            var response = await client.GetMinuteAsync(code);

            return response.Items.Select(item => new IntradayData
            {
                Code = code,
                Time = item.Time,
                Price = (decimal)item.Price,
                CumulativeVolume = item.Volume * 100,
                Source = ProviderId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get intraday for {Code}", code);
            return new List<IntradayData>();
        }
    }

    public async Task<List<IntradayData>> GetHistoryIntradayAsync(string date, string code)
    {
        try
        {
            var client = await GetClientAsync();
            var response = await client.GetHistoryMinuteAsync(date, code);

            return response.Items.Select(item => new IntradayData
            {
                Code = code,
                Time = item.Time,
                Price = (decimal)item.Price,
                CumulativeVolume = item.Volume * 100,
                Source = ProviderId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get history intraday for {Code} on {Date}", code, date);
            return new List<IntradayData>();
        }
    }

    public async Task<List<TradeData>> GetTradesAsync(string code, int start = 0, int count = 100)
    {
        try
        {
            var client = await GetClientAsync();
            var response = await client.GetMinuteTradesAsync(code, (ushort)start, (ushort)count);

            return response.Items.Select(item => new TradeData
            {
                Code = code,
                Time = item.Time,
                Price = (decimal)item.Price,
                Volume = item.Volume * 100,
                OrderCount = item.Number,
                Direction = item.Status switch
                {
                    0 => 1,   // 买入
                    1 => -1,  // 卖出
                    _ => 0    // 中性
                },
                Source = ProviderId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get trades for {Code}", code);
            return new List<TradeData>();
        }
    }

    public async Task<List<TradeData>> GetHistoryTradesAsync(string date, string code, int start = 0, int count = 100)
    {
        try
        {
            var client = await GetClientAsync();
            var response = await client.GetHistoryMinuteTradesAsync(date, code, (ushort)start, (ushort)count);

            return response.Items.Select(item => new TradeData
            {
                Code = code,
                Time = item.Time,
                Price = (decimal)item.Price,
                Volume = item.Volume * 100,
                OrderCount = item.Number,
                Direction = item.Status switch
                {
                    0 => 1,
                    1 => -1,
                    _ => 0
                },
                Source = ProviderId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get history trades for {Code} on {Date}", code, date);
            return new List<TradeData>();
        }
    }

    public async Task<List<CallAuctionData>> GetCallAuctionAsync(string code)
    {
        try
        {
            var client = await GetClientAsync();
            var response = await client.GetCallAuctionAsync(code);

            return response.Items.Select(item => new CallAuctionData
            {
                Code = code,
                Time = item.Time,
                Price = (decimal)item.Price,
                MatchVolume = item.MatchVolume,
                UnmatchedVolume = item.UnmatchedVolume,
                Flag = item.Flag,
                Source = ProviderId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get call auction for {Code}", code);
            return new List<CallAuctionData>();
        }
    }

    public async Task<List<StockCodeInfo>> GetStockCodesAsync(string market)
    {
        try
        {
            var client = await GetClientAsync();
            var exchange = TdxCode.ExchangeFromText(market);
            var count = await client.GetCodeCountAsync(exchange);
            var result = new List<StockCodeInfo>();
            var start = (ushort)0;

            while (start < count)
            {
                var response = await client.GetCodesAsync(exchange, start);
                result.AddRange(response.Items.Select(item =>
                {
                    var fullCode = exchange.ToPrefix() + item.Code;
                    return new StockCodeInfo
                    {
                        Code = fullCode,
                        Name = item.Name,
                        Market = exchange.ToPrefix(),
                        SecurityType = GetSecurityType(fullCode),
                        DecimalPlaces = item.Decimal,
                        LastPrice = (decimal)item.LastPrice
                    };
                }));
                start += (ushort)response.Items.Count;

                if (response.Items.Count == 0)
                    break;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stock codes for {Market}", market);
            return new List<StockCodeInfo>();
        }
    }

    /// <summary>
    /// 获取证券类型
    /// </summary>
    private static string GetSecurityType(string code)
    {
        if (TdxCode.IsIndex(code))
            return "Index";

        if (TdxCode.IsEtf(code))
            return "ETF";

        return "Stock";
    }

    public async Task<List<StockCodeInfo>> GetStockCodesAsync(string market, SecurityType securityType)
    {
        var allCodes = await GetStockCodesAsync(market);
        var typeStr = securityType.ToString();
        return allCodes.Where(x => x.SecurityType.Equals(typeStr, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<StockInfo>> GetStockListAsync()
    {
        try
        {
            if (!_stockCache.IsEmpty)
                return _stockCache.Values.ToList();

            var client = await GetClientAsync();

            foreach (var exchange in new[] { TdxExchange.Sh, TdxExchange.Sz })
            {
                var count = await client.GetCodeCountAsync(exchange);
                var start = (ushort)0;

                while (start < count)
                {
                    var response = await client.GetCodesAsync(exchange, start);
                    foreach (var item in response.Items)
                    {
                        var stockInfo = new StockInfo
                        {
                            Code = item.Code,
                            Name = item.Name,
                            Market = exchange.ToPrefix()
                        };
                        _stockCache.TryAdd(item.Code, stockInfo);
                    }
                    start += (ushort)response.Items.Count;

                    if (response.Items.Count == 0)
                        break;
                }
            }

            return _stockCache.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stock list");
            return new List<StockInfo>();
        }
    }

    public Task<bool> IsTradingDayAsync(DateTime date)
    {
        return Task.FromResult(date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday);
    }

    public Task<CapitalFlowData?> GetCapitalFlowAsync(string code)
    {
        // 通达信不支持资金流向数据
        return Task.FromResult<CapitalFlowData?>(null);
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var client = await GetClientAsync();
            var quotes = await client.GetQuotesAsync(new[] { "600519" });
            return quotes.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.DisposeAsync().AsTask().Wait();
            _lock.Dispose();
            _disposed = true;
        }
    }
}
