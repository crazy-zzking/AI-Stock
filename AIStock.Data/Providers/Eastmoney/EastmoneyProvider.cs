using System.Net;
using System.Net.Http;
using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Data.Providers;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers.Eastmoney;

/// <summary>
/// 东方财富数据提供者
/// </summary>
public class EastmoneyProvider : BaseProvider
{
    private const string QuoteUrl = "https://push2.eastmoney.com/api/qt/stock/get";
    private const string KlineUrl = "https://push2his.eastmoney.com/api/qt/stock/kline/get";
    private const string IntradayUrl = "https://push2.eastmoney.com/api/qt/stock/trends2/get";
    private const string CapitalFlowUrl = "https://push2.eastmoney.com/api/qt/stock/fflow/kline/get";
    private const string UserToken = "fa5fd1943c7b386f172d6893dbfba10b";

    private readonly HttpClient _httpClient;

    public EastmoneyProvider(
        ILogger<EastmoneyProvider> logger,
        HttpClient httpClient)
        : base(logger, httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 创建带代理的HttpClient
    /// </summary>
    public static HttpClient CreateHttpClientWithProxy(string proxyHost, int proxyPort, string? username = null, string? password = null)
    {
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(proxyHost, proxyPort)
            {
                Credentials = string.IsNullOrEmpty(username) ? null : new NetworkCredential(username, password)
            },
            UseProxy = true
        };
        return new HttpClient(handler);
    }

    /// <summary>
    /// 创建带隧道代理的HttpClient
    /// </summary>
    public static HttpClient CreateHttpClientWithTunnelProxy(string tunnelHost, int tunnelPort, string username, string password)
    {
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"{tunnelHost}:{tunnelPort}")
            {
                Credentials = new NetworkCredential(username, password)
            },
            UseProxy = true
        };
        return new HttpClient(handler);
    }

    public override string ProviderId => "eastmoney";
    public override string ProviderName => "东方财富";

    public override IEnumerable<DataCapability> Capabilities => new[]
    {
        DataCapability.Quote,
        DataCapability.Kline,
        DataCapability.Intraday,
        DataCapability.CapitalFlow
    };

    /// <summary>
    /// 获取实时行情
    /// </summary>
    public override async Task<QuoteData?> GetQuoteAsync(string code)
    {
        try
        {
            var secid = GetMarketCode(code);
            var url = $"{QuoteUrl}?secid={secid}&fields=f43,f44,f45,f46,f47,f48,f50,f51,f52,f55,f57,f58,f60,f116,f117,f170";
            Logger.LogDebug("Requesting quote from: {Url}", url);
            
            var response = await SendEastmoneyRequestAsync(url);

            if (response == null)
            {
                Logger.LogWarning("No response from eastmoney for {Code}", code);
                return null;
            }
            
            Logger.LogDebug("Response for {Code}: {Response}", code, response[..Math.Min(500, response.Length)]);

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            {
                Logger.LogWarning("No data field in response for {Code}", code);
                return null;
            }

            // 安全获取数值 - 支持多种格式
            decimal GetDecimal(JsonElement el, int divisor = 1)
            {
                try
                {
                    if (el.ValueKind == JsonValueKind.Number)
                        return el.GetDecimal() / divisor;
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var str = el.GetString();
                        if (decimal.TryParse(str, out var val))
                            return val / divisor;
                    }
                    if (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                        return 0;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to parse value: {Value}", el.ToString());
                }
                return 0;
            }

            var quote = new QuoteData
            {
                Code = code,
                Name = data.TryGetProperty("f58", out var nameEl) ? nameEl.GetString() ?? "" : "",
                Price = GetDecimal(data.GetProperty("f43"), 100),
                PreClose = GetDecimal(data.GetProperty("f60"), 100),
                Open = GetDecimal(data.GetProperty("f46"), 100),
                High = GetDecimal(data.GetProperty("f44"), 100),
                Low = GetDecimal(data.GetProperty("f45"), 100),
                Volume = (long)GetDecimal(data.GetProperty("f47")) * 100,
                Amount = GetDecimal(data.GetProperty("f48")),
                ChangePercent = GetDecimal(data.GetProperty("f170"), 100),
                ChangeAmount = GetDecimal(data.GetProperty("f116"), 100),
                TurnoverRate = GetDecimal(data.GetProperty("f117"), 100),
                VolumeRatio = GetDecimal(data.GetProperty("f50"), 100),
                Timestamp = DateTime.UtcNow,
                Source = ProviderId
            };

            return quote;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get quote for {Code}", code);
            return null;
        }
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public override async Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100)
    {
        try
        {
            var secid = GetMarketCode(code);
            var klt = GetKlineInterval(interval);
            var url = $"{KlineUrl}?fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61&end=20500101&ut={UserToken}&rtntype=6&secid={secid}&klt={klt}&fqt=1&lmt={count}";
            var response = await SendEastmoneyRequestAsync(url);

            if (response == null)
                return new List<KlineData>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return new List<KlineData>();

            var klines = data.GetProperty("klines");
            var result = new List<KlineData>();

            foreach (var item in klines.EnumerateArray())
            {
                var parts = item.GetString()?.Split(',');
                if (parts == null || parts.Length < 7) continue;

                var kline = new KlineData
                {
                    Code = code,
                    DateTime = DateTime.Parse(parts[0]),
                    Open = decimal.Parse(parts[1]),
                    Close = decimal.Parse(parts[2]),
                    High = decimal.Parse(parts[3]),
                    Low = decimal.Parse(parts[4]),
                    Volume = long.Parse(parts[5]),
                    Amount = decimal.Parse(parts[6]),
                    Source = ProviderId
                };

                if (parts.Length > 7)
                    kline.TurnoverRate = decimal.Parse(parts[7]);
                if (parts.Length > 8)
                    kline.ChangePercent = decimal.Parse(parts[8]);

                result.Add(kline);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get klines for {Code}", code);
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
            var secid = GetMarketCode(code);
            var url = $"{IntradayUrl}?fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13&fields2=f51,f52,f53,f54,f55,f56,f57,f58&ut={UserToken}&secid={secid}&ndays=1&iscr=1&iscca=0";
            var response = await SendEastmoneyRequestAsync(url);

            if (response == null)
                return new List<IntradayData>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return new List<IntradayData>();

            var trends = data.GetProperty("trends");
            var preClose = data.GetProperty("preClose").GetInt64() / 1000m;
            var result = new List<IntradayData>();

            foreach (var item in trends.EnumerateArray())
            {
                var parts = item.GetString()?.Split(',');
                if (parts == null || parts.Length < 6) continue;

                var price = decimal.Parse(parts[1]);
                var intraday = new IntradayData
                {
                    Code = code,
                    Time = DateTime.TryParse(parts[0], out var time) ? time : DateTime.MinValue,
                    Price = price,
                    CumulativeVolume = long.Parse(parts[2]) * 100,
                    CumulativeAmount = decimal.Parse(parts[3]),
                    AveragePrice = decimal.Parse(parts[4]),
                    ChangePercent = preClose > 0 ? (price - preClose) / preClose * 100 : 0,
                    Source = ProviderId
                };

                result.Add(intraday);
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
    /// 获取资金流向
    /// </summary>
    public override async Task<CapitalFlowData?> GetCapitalFlowAsync(string code)
    {
        try
        {
            var secid = GetMarketCode(code);
            var url = $"{CapitalFlowUrl}?lmt=0&klt=1&secid={secid}&fields1=f1,f2,f3,f7&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61,f62,f63,f64,f65";
            var response = await SendEastmoneyRequestAsync(url);

            if (response == null)
                return null;

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return null;

            var klines = data.GetProperty("klines");
            var last = klines.EnumerateArray().LastOrDefault();

            if (last.ValueKind == JsonValueKind.Null)
                return null;

            var parts = last.GetString()?.Split(',');
            if (parts == null || parts.Length < 7) return null;

            var flow = new CapitalFlowData
            {
                Code = code,
                Date = DateTime.Parse(parts[0]),
                MainNetInflow = decimal.Parse(parts[1]),
                SuperLargeNetInflow = decimal.Parse(parts[2]),
                LargeNetInflow = decimal.Parse(parts[3]),
                MediumNetInflow = decimal.Parse(parts[4]),
                SmallNetInflow = decimal.Parse(parts[5]),
                Source = ProviderId
            };

            return flow;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get capital flow for {Code}", code);
            return null;
        }
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public override async Task<bool> IsHealthyAsync()
    {
        try
        {
            var url = $"{QuoteUrl}?secid=1.600519&fields=f43";
            var response = await SendEastmoneyRequestAsync(url);
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 发送带东方财富所需请求头的 HTTP 请求
    /// </summary>
    private async Task<string?> SendEastmoneyRequestAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Referer", "https://quote.eastmoney.com/");
            request.Headers.Add("Accept", "application/json, text/plain, */*");

            var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send Eastmoney request to {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// 获取K线周期映射
    /// </summary>
    private static int GetKlineInterval(KlineInterval interval)
    {
        return interval switch
        {
            KlineInterval.Min1 => 1,
            KlineInterval.Min5 => 5,
            KlineInterval.Min15 => 15,
            KlineInterval.Min30 => 30,
            KlineInterval.Min60 => 60,
            KlineInterval.Daily => 101,
            KlineInterval.Weekly => 102,
            KlineInterval.Monthly => 103,
            _ => 101
        };
    }
}
