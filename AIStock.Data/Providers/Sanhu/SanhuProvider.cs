using System.Net;
using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Data.Providers;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers.Sanhu;

/// <summary>
/// 散户量化数据提供者
/// </summary>
public class SanhuProvider : BaseProvider
{
    private readonly string _baseUrl;
    private readonly string _token;

    public SanhuProvider(
        ILogger<SanhuProvider> logger,
        HttpClient httpClient,
        string baseUrl,
        string token,
        IWebProxy? proxy = null)
        : base(logger, httpClient, proxy)
    {
        _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public override string ProviderId => "sanhu";
    public override string ProviderName => "散户量化";

    public override IEnumerable<DataCapability> Capabilities => new[]
    {
        DataCapability.Intraday,
        DataCapability.TradingCalendar,
        DataCapability.StockUniverse
    };

    /// <summary>
    /// 获取实时行情（散户量化不支持）
    /// </summary>
    public override Task<QuoteData?> GetQuoteAsync(string code)
    {
        return Task.FromResult<QuoteData?>(null);
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public override Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100)
    {
        // 散户量化只支持日K
        if (interval != KlineInterval.Daily)
            throw new NotSupportedException("SanhuQuant only supports daily kline");

        return GetDailyKlinesAsync(code, count);
    }

    /// <summary>
    /// 获取日K线数据
    /// </summary>
    private async Task<List<KlineData>> GetDailyKlinesAsync(string code, int count)
    {
        try
        {
            var url = $"{_baseUrl}/v1/hsa_rixian?token={_token}&code={code}&all=0";
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<KlineData>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return new List<KlineData>();

            var data = root.GetProperty("data");
            var result = new List<KlineData>();

            foreach (var item in data.EnumerateArray())
            {
                var kline = new KlineData
                {
                    Code = code,
                    DateTime = DateTime.Parse(item.GetProperty("RiQi").GetString()!),
                    Open = item.GetProperty("KaiPan").GetInt64() / 1000m,
                    Close = item.GetProperty("ShouPan").GetInt64() / 1000m,
                    High = item.GetProperty("ZuiGao").GetInt64() / 1000m,
                    Low = item.GetProperty("ZuiDi").GetInt64() / 1000m,
                    Volume = item.GetProperty("ZongLiang").GetInt64() * 100,
                    Amount = item.GetProperty("JinE").GetInt64(),
                    Source = ProviderId
                };
                result.Add(kline);
            }

            return result.TakeLast(count).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get daily klines for {Code}", code);
            return new List<KlineData>();
        }
    }

    /// <summary>
    /// 获取分时数据
    /// </summary>
    public override async Task<List<IntradayData>> GetIntradayAsync(string code)
    {
        try
        {
            var url = $"{_baseUrl}/v1/hsa_fenshi?token={_token}&code={code}&all=1";
            Logger.LogDebug("Requesting intraday from: {Url}", url);
            
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<IntradayData>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("ret", out var retProp) || retProp.GetInt32() != 200)
                return new List<IntradayData>();

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new List<IntradayData>();

            var result = new List<IntradayData>();

            foreach (var item in data.EnumerateArray())
            {
                try
                {
                    // 安全获取数值
                    long GetLong(string name) => item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;
                    decimal GetDecimal(string name) => item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() / 1000m : 0;
                    string GetString(string name) => item.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

                    var intraday = new IntradayData
                    {
                        Code = code,
                        Time = GetString("ShiJian"),
                        Price = GetDecimal("JiaGe"),
                        CumulativeVolume = GetLong("ZongLiang") * 100,
                        CumulativeAmount = GetLong("JinE"),
                        TurnoverRate = GetLong("HuanShou"),
                        VolumeRatio = GetLong("LiangBi"),
                        InnerVolume = GetLong("NeiPan"),
                        OuterVolume = GetLong("WaiPan"),
                        WeiBi = GetLong("WeiBi"),
                        ChangePercent = GetLong("ZhangFu"),
                        ChangeSpeed = GetLong("ZhangSu"),
                        AveragePrice = GetDecimal("JunJia"),
                        Source = ProviderId
                    };
                    result.Add(intraday);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to parse intraday item");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get intraday data for {Code}", code);
            return new List<IntradayData>();
        }
    }

    /// <summary>
    /// 获取股票列表
    /// </summary>
    public override async Task<List<StockInfo>> GetStockListAsync()
    {
        try
        {
            var url = $"{_baseUrl}/v1/hsa_gupiao?token={_token}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<StockInfo>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return new List<StockInfo>();

            var data = root.GetProperty("data");
            var result = new List<StockInfo>();

            foreach (var item in data.EnumerateArray())
            {
                var stock = new StockInfo
                {
                    Code = item.GetProperty("code").GetString()!,
                    Name = item.GetProperty("name").GetString()!,
                    Market = GetPrefixCode(item.GetProperty("code").GetString()!).Substring(0, 2)
                };
                result.Add(stock);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get stock list");
            return new List<StockInfo>();
        }
    }

    /// <summary>
    /// 检查是否为交易日
    /// </summary>
    public override async Task<bool> IsTradingDayAsync(DateTime date)
    {
        try
        {
            var url = $"{_baseUrl}/v1/hsa_rili?token={_token}&date={date:yyyy-MM-dd}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return false;

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return false;

            return root.GetProperty("data").GetProperty("trade").GetInt32() == 1;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to check trading day for {Date}", date);
            return false;
        }
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public override async Task<bool> IsHealthyAsync()
    {
        try
        {
            var url = $"{_baseUrl}/v1/hsa_gupiao?token={_token}";
            var response = await SendRequestAsync(url);
            return response != null;
        }
        catch
        {
            return false;
        }
    }
}
