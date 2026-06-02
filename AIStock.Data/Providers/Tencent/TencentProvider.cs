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
            return ParseQuote(parts, code);
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
                var quote = ParseQuote(parts, codeList[i]);
                if (quote != null) results.Add(quote);
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
    /// 解析腾讯 q= 行情字段（~ 分隔）。索引为腾讯实测约定，盘中联调请核对一只样本。
    /// 含扩展字段：换手率[38]/PE TTM[39]/流通市值亿[44]/总市值亿[45]/市净率[46]/量比[49]。
    /// </summary>
    private QuoteData? ParseQuote(string[] parts, string code)
    {
        if (parts.Length < 38) return null;
        decimal D(int i) => i < parts.Length && decimal.TryParse(parts[i], out var v) ? v : 0;
        long L(int i) => i < parts.Length && long.TryParse(parts[i], out var v) ? v : 0;

        return new QuoteData
        {
            Code = code,
            Name = parts[1],
            Price = D(3),
            PreClose = D(4),
            Open = D(5),
            Volume = L(6) * 100,
            OuterVolume = L(7) * 100,
            InnerVolume = L(8) * 100,
            High = D(33),
            Low = D(34),
            ChangePercent = D(32),
            ChangeAmount = D(31),
            Amount = D(37) * 10_000m, // [37] 累计成交额(万元) → 元
            TurnoverRate = D(38),
            PeTtm = D(39),
            FloatMarketCap = D(44) * 100_000_000m, // 亿 → 元
            TotalMarketCap = D(45) * 100_000_000m,
            Pb = D(46),
            VolumeRatio = D(49),
            AvgPrice = D(51), // 当日均价
            Timestamp = DateTime.Now,
            Source = ProviderId
        };
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

                var dateStr = item[0].GetString();
                var openStr = item[1].GetString();
                var closeStr = item[2].GetString();
                var highStr = item[3].GetString();
                var lowStr = item[4].GetString();
                var volumeStr = item[5].GetString();

                if (!DateTime.TryParse(dateStr, out var dateTime) ||
                    !decimal.TryParse(openStr, out var open) ||
                    !decimal.TryParse(closeStr, out var close) ||
                    !decimal.TryParse(highStr, out var high) ||
                    !decimal.TryParse(lowStr, out var low) ||
                    !decimal.TryParse(volumeStr, out var volume))
                {
                    Logger.LogWarning("Failed to parse kline data: {Item}", item);
                    continue;
                }

                var kline = new KlineData
                {
                    Code = code,
                    DateTime = dateTime,
                    Open = open,
                    Close = close,
                    High = high,
                    Low = low,
                    Volume = (long)(volume * 100),
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
