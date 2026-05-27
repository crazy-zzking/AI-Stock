using System.Net;
using System.Text;
using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Data.Providers;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers.Tencent;

/// <summary>
/// 腾讯财经数据提供者
/// </summary>
public class TencentProvider : BaseProvider
{
    private const string QuoteUrl = "http://qt.gtimg.cn/q=";
    private const string KlineUrl = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get";

    public TencentProvider(
        ILogger<TencentProvider> logger,
        HttpClient httpClient,
        IWebProxy? proxy = null)
        : base(logger, httpClient, proxy)
    {
        // 设置GBK编码支持
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public override string ProviderId => "tencent";
    public override string ProviderName => "腾讯财经";

    public override IEnumerable<DataCapability> Capabilities => new[]
    {
        DataCapability.Quote,
        DataCapability.Kline
    };

    /// <summary>
    /// 获取实时行情
    /// </summary>
    public override async Task<QuoteData?> GetQuoteAsync(string code)
    {
        try
        {
            var prefixCode = GetPrefixCode(code);
            var url = $"{QuoteUrl}{prefixCode}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return null;

            // 解析腾讯行情数据（~分隔）
            var parts = response.Split('~');
            if (parts.Length < 38) return null;

            var quote = new QuoteData
            {
                Code = code,
                Name = parts[1],
                Price = decimal.Parse(parts[3]),
                PreClose = decimal.Parse(parts[4]),
                Open = decimal.Parse(parts[5]),
                Volume = long.Parse(parts[6]) * 100,
                OuterVolume = long.Parse(parts[7]) * 100,
                InnerVolume = long.Parse(parts[8]) * 100,
                High = decimal.Parse(parts[33]),
                Low = decimal.Parse(parts[34]),
                ChangePercent = decimal.Parse(parts[32]),
                ChangeAmount = decimal.Parse(parts[31]),
                Amount = decimal.Parse(parts[37]),
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
    /// 批量获取实时行情
    /// </summary>
    public override async Task<List<QuoteData>> GetQuotesAsync(IEnumerable<string> codes)
    {
        try
        {
            var codeList = codes.ToList();
            var prefixCodes = codeList.Select(GetPrefixCode);
            var url = $"{QuoteUrl}{string.Join(",", prefixCodes)}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<QuoteData>();

            var results = new List<QuoteData>();
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < Math.Min(lines.Length, codeList.Count); i++)
            {
                var parts = lines[i].Split('~');
                if (parts.Length < 38) continue;

                var quote = new QuoteData
                {
                    Code = codeList[i],
                    Name = parts[1],
                    Price = decimal.Parse(parts[3]),
                    PreClose = decimal.Parse(parts[4]),
                    Open = decimal.Parse(parts[5]),
                    Volume = long.Parse(parts[6]) * 100,
                    OuterVolume = long.Parse(parts[7]) * 100,
                    InnerVolume = long.Parse(parts[8]) * 100,
                    High = decimal.Parse(parts[33]),
                    Low = decimal.Parse(parts[34]),
                    ChangePercent = decimal.Parse(parts[32]),
                    ChangeAmount = decimal.Parse(parts[31]),
                    Amount = decimal.Parse(parts[37]),
                    Timestamp = DateTime.UtcNow,
                    Source = ProviderId
                };

                results.Add(quote);
            }

            return results;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get batch quotes");
            return new List<QuoteData>();
        }
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public override async Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100)
    {
        try
        {
            if (interval != KlineInterval.Daily)
            {
                Logger.LogWarning("Tencent only supports daily kline");
                return new List<KlineData>();
            }

            var prefixCode = GetPrefixCode(code);
            var url = $"{KlineUrl}?param={prefixCode},day,,,{count},qfq";
            Logger.LogDebug("Requesting klines from: {Url}", url);
            
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<KlineData>();

            var jsonDoc = System.Text.Json.JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                return new List<KlineData>();

            var stockData = data.GetProperty(prefixCode);
            
            // 腾讯API返回的字段名可能是 day 或 qfqday
            JsonElement klines;
            if (stockData.TryGetProperty("qfqday", out var qfqday))
                klines = qfqday;
            else if (stockData.TryGetProperty("day", out var day))
                klines = day;
            else
                return new List<KlineData>();

            var result = new List<KlineData>();
            foreach (var item in klines.EnumerateArray())
            {
                if (item.GetArrayLength() < 6) continue;
                
                var kline = new KlineData
                {
                    Code = code,
                    DateTime = DateTime.Parse(item[0].GetString()!),
                    Open = decimal.Parse(item[1].GetString()!),
                    Close = decimal.Parse(item[2].GetString()!),
                    High = decimal.Parse(item[3].GetString()!),
                    Low = decimal.Parse(item[4].GetString()!),
                    Volume = (long)(decimal.Parse(item[5].GetString()!) * 100),
                    Source = ProviderId
                };
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
    public override Task<List<IntradayData>> GetIntradayAsync(string code)
    {
        // 腾讯不支持分时数据
        Logger.LogWarning("Tencent does not support intraday data");
        return Task.FromResult(new List<IntradayData>());
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public override async Task<bool> IsHealthyAsync()
    {
        try
        {
            var url = $"{QuoteUrl}sh600519";
            var response = await SendRequestAsync(url);
            return response != null;
        }
        catch
        {
            return false;
        }
    }
}
