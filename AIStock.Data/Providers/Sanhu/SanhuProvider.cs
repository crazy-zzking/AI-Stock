using System.Net;
using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
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
    private readonly string _mykey;

    public SanhuProvider(
        ILogger<SanhuProvider> logger,
        HttpClient httpClient,
        string baseUrl,
        string token,
        string mykey = "",
        IWebProxy? proxy = null)
        : base(logger, httpClient, proxy)
    {
        _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _mykey = mykey ?? "";
    }

    public override string ProviderId => "sanhu";
    public override string ProviderName => "散户量化";

    public override IEnumerable<DataCapability> Capabilities => new[]
    {
        DataCapability.Intraday,
        DataCapability.TradingCalendar,
        DataCapability.StockUniverse,
        DataCapability.Trading
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

                    var timeStr = GetString("ShiJian");
                    var intraday = new IntradayData
                    {
                        Code = code,
                        Time = DateTime.TryParse(timeStr, out var time) ? time : DateTime.MinValue,
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

    /// <summary>
    /// 获取账户信息（持仓+资金）
    /// </summary>
    public override async Task<AccountInfo> GetAccountInfoAsync()
    {
        try
        {
            var url = $"{_baseUrl}/v1/jycx_chicang?token={_token}";
            Logger.LogInformation("Requesting account info from: {Url}", url);
            var response = await SendRequestAsync(url);

            if (response == null)
                return new AccountInfo();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return new AccountInfo();

            var result = new AccountInfo();

            // 提取 base 字段的资金信息
            if (root.TryGetProperty("base", out var baseData))
            {
                result.TotalAssets = baseData.TryGetProperty("ZongZhi", out var zongzhi) ? zongzhi.GetDecimal() : 0;
                result.TotalProfit = baseData.TryGetProperty("YingLi", out var yingli) ? yingli.GetDecimal() : 0;
                result.AvailableBalance = baseData.TryGetProperty("ZiJin", out var zijin) ? zijin.GetDecimal() : 0;
            }

            // 提取 data 字段的持仓信息（可能为空或不存在）
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var position = new AccountPosition
                    {
                        Code = item.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "",
                        Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                        Volume = item.TryGetProperty("GuShu", out var gushu) ? gushu.GetInt64() : 0,
                        AvailableVolume = item.TryGetProperty("KeMai", out var kemai) ? kemai.GetInt64() : 0,
                        CostPrice = item.TryGetProperty("ChengBen", out var chengben) ? chengben.GetInt64() / 1000m : 0,
                        CurrentPrice = item.TryGetProperty("JiaGe", out var jiage) ? jiage.GetInt64() / 1000m : 0,
                        Profit = item.TryGetProperty("YingKui", out var yingkui) ? yingkui.GetInt64() : 0,
                        ProfitRate = item.TryGetProperty("LiRunLv", out var lirunlv) ? lirunlv.GetInt64() / 1000m : 0
                    };
                    position.MarketValue = position.Volume * position.CurrentPrice;
                    result.Positions.Add(position);
                }
            }

            Logger.LogInformation("Got account info: TotalAssets={TotalAssets}, Positions={Count}",
                result.TotalAssets, result.Positions.Count);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get account info");
            return new AccountInfo();
        }
    }

    /// <summary>
    /// 即时买入
    /// </summary>
    public async Task<SanhuOrderResult> PlaceBuyOrderAsync(string code, decimal price, int hands, string? policy = null)
    {
        try
        {
            var priceInt = (int)(price * 1000);
            var url = $"{_baseUrl}/v1/ssjy_jimairu?token={_token}&mykey={_mykey}&code={code}&price={priceInt}&hand={hands}";
            if (!string.IsNullOrEmpty(policy))
                url += $"&policy={Uri.EscapeDataString(policy)}";

            var response = await SendRequestAsync(url);
            if (response == null)
                return new SanhuOrderResult { Ret = -1, Msg = "请求失败" };

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            return new SanhuOrderResult
            {
                Ret = root.GetProperty("ret").GetInt32(),
                Msg = root.TryGetProperty("msg", out var msg) ? msg.GetString() ?? "" : "",
                OrderId = root.TryGetProperty("orderid", out var oid) ? oid.GetInt64() : 0,
                AgreeId = root.TryGetProperty("agreeid", out var aid) ? aid.GetInt64() : 0,
                Time = root.TryGetProperty("time", out var time) ? time.GetString() : null,
                Type = root.TryGetProperty("type", out var type) ? type.GetString() : null,
                Code = root.TryGetProperty("code", out var c) ? c.GetString() : null,
                Price = root.TryGetProperty("price", out var p) ? p.GetInt32() : 0,
                Hands = root.TryGetProperty("hand", out var h) ? h.GetInt32() : 0,
                Policy = root.TryGetProperty("policy", out var pol) ? pol.GetString() : null
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to place buy order for {Code}", code);
            return new SanhuOrderResult { Ret = -1, Msg = ex.Message };
        }
    }

    /// <summary>
    /// 即时卖出
    /// </summary>
    public async Task<SanhuOrderResult> PlaceSellOrderAsync(string code, decimal price, int hands, string? policy = null)
    {
        try
        {
            var priceInt = (int)(price * 1000);
            var url = $"{_baseUrl}/v1/ssjy_jimaichu?token={_token}&mykey={_mykey}&code={code}&price={priceInt}&hand={hands}";
            if (!string.IsNullOrEmpty(policy))
                url += $"&policy={Uri.EscapeDataString(policy)}";

            var response = await SendRequestAsync(url);
            if (response == null)
                return new SanhuOrderResult { Ret = -1, Msg = "请求失败" };

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            return new SanhuOrderResult
            {
                Ret = root.GetProperty("ret").GetInt32(),
                Msg = root.TryGetProperty("msg", out var msg) ? msg.GetString() ?? "" : "",
                OrderId = root.TryGetProperty("orderid", out var oid) ? oid.GetInt64() : 0,
                AgreeId = root.TryGetProperty("agreeid", out var aid) ? aid.GetInt64() : 0,
                Time = root.TryGetProperty("time", out var time) ? time.GetString() : null,
                Type = root.TryGetProperty("type", out var type) ? type.GetString() : null,
                Code = root.TryGetProperty("code", out var c) ? c.GetString() : null,
                Price = root.TryGetProperty("price", out var p) ? p.GetInt32() : 0,
                Hands = root.TryGetProperty("hand", out var h) ? h.GetInt32() : 0,
                Policy = root.TryGetProperty("policy", out var pol) ? pol.GetString() : null
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to place sell order for {Code}", code);
            return new SanhuOrderResult { Ret = -1, Msg = ex.Message };
        }
    }

    /// <summary>
    /// 查询订单状态
    /// </summary>
    public async Task<SanhuOrderResult?> QueryOrderSanhuAsync(long orderId)
    {
        try
        {
            var url = $"{_baseUrl}/v1/jycx_chadan?token={_token}&orderid={orderId}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return null;

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            return new SanhuOrderResult
            {
                Ret = root.GetProperty("ret").GetInt32(),
                OrderId = orderId,
                AgreeId = root.TryGetProperty("agreeid", out var aid) ? aid.GetInt64() : 0,
                Time = root.TryGetProperty("time", out var time) ? time.GetString() : null,
                Type = root.TryGetProperty("type", out var type) ? type.GetString() : null,
                Code = root.TryGetProperty("code", out var c) ? c.GetString() : null,
                Price = root.TryGetProperty("price", out var p) ? p.GetInt32() : 0,
                Hands = root.TryGetProperty("hand", out var h) ? h.GetInt32() : 0,
                Policy = root.TryGetProperty("policy", out var pol) ? pol.GetString() : null
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to query order {OrderId}", orderId);
            return null;
        }
    }

    /// <summary>
    /// 获取可撤委托列表
    /// </summary>
    public async Task<List<SanhuEntrustment>> GetCancelableOrdersAsync()
    {
        try
        {
            var url = $"{_baseUrl}/v1/jycx_keche?token={_token}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<SanhuEntrustment>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return new List<SanhuEntrustment>();

            var data = root.GetProperty("data");
            var result = new List<SanhuEntrustment>();

            foreach (var item in data.EnumerateArray())
            {
                result.Add(new SanhuEntrustment
                {
                    AgreeId = item.GetProperty("agreeid").GetInt64(),
                    Code = item.GetProperty("code").GetString() ?? "",
                    Name = item.GetProperty("name").GetString() ?? "",
                    Type = item.GetProperty("type").GetString() ?? "",
                    Price = item.GetProperty("price").GetInt64() / 1000m,
                    Volume = item.GetProperty("volume").GetInt64()
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get cancelable orders");
            return new List<SanhuEntrustment>();
        }
    }

    /// <summary>
    /// 获取今日成交记录
    /// </summary>
    public async Task<List<SanhuTrade>> GetTodayTradesSanhuAsync()
    {
        try
        {
            var url = $"{_baseUrl}/v1/jycx_jrcj?token={_token}";
            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<SanhuTrade>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return new List<SanhuTrade>();

            return ParseTrades(root.GetProperty("data"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get today trades");
            return new List<SanhuTrade>();
        }
    }

    /// <summary>
    /// 获取近期成交记录
    /// </summary>
    public async Task<List<SanhuTrade>> GetRecentTradesAsync(string? code = null)
    {
        try
        {
            var url = $"{_baseUrl}/v1/jycx_cjjl?token={_token}";
            if (!string.IsNullOrEmpty(code))
                url += $"&code={code}";

            var response = await SendRequestAsync(url);

            if (response == null)
                return new List<SanhuTrade>();

            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.GetProperty("ret").GetInt32() != 200)
                return new List<SanhuTrade>();

            return ParseTrades(root.GetProperty("data"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get recent trades");
            return new List<SanhuTrade>();
        }
    }

    private List<SanhuTrade> ParseTrades(JsonElement data)
    {
        var result = new List<SanhuTrade>();

        foreach (var item in data.EnumerateArray())
        {
            result.Add(new SanhuTrade
            {
                AgreeId = item.TryGetProperty("agreeid", out var aid) ? aid.GetInt64() : 0,
                Time = item.TryGetProperty("time", out var time) ? time.GetString() : null,
                Code = item.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                Price = item.TryGetProperty("price", out var p) ? p.GetInt64() / 1000m : 0,
                Volume = item.TryGetProperty("volume", out var v) ? v.GetInt64() : 0
            });
        }

        return result;
    }

    // IDataProvider 交易接口实现
    public override async Task<TradingOrderResult> PlaceBuyOrderAsync(string code, decimal price, int volume)
    {
        var hands = volume / 100;
        if (hands <= 0) hands = 1;
        var result = await PlaceBuyOrderAsync(code, price, hands);
        return new TradingOrderResult
        {
            OrderId = result.OrderId,
            IsAccepted = result.IsAccepted,
            IsCompleted = result.IsCompleted,
            IsFailed = result.IsFailed,
            Msg = result.Msg
        };
    }

    public override async Task<TradingOrderResult> PlaceSellOrderAsync(string code, decimal price, int volume)
    {
        var hands = volume / 100;
        if (hands <= 0) hands = 1;
        var result = await PlaceSellOrderAsync(code, price, hands);
        return new TradingOrderResult
        {
            OrderId = result.OrderId,
            IsAccepted = result.IsAccepted,
            IsCompleted = result.IsCompleted,
            IsFailed = result.IsFailed,
            Msg = result.Msg
        };
    }

    public override async Task<TradingOrderStatus?> QueryOrderAsync(long orderId)
    {
        var result = await QueryOrderSanhuAsync(orderId);
        if (result == null) return null;
        return new TradingOrderStatus
        {
            OrderId = result.OrderId,
            IsPending = result.IsAccepted,
            IsCompleted = result.IsCompleted,
            IsFailed = result.IsFailed
        };
    }

    public override async Task<List<TradingTradeInfo>> GetTodayTradesAsync()
    {
        var trades = await GetTodayTradesSanhuAsync();
        return trades.Select(t => new TradingTradeInfo
        {
            AgreeId = t.AgreeId,
            Code = t.Code,
            Type = t.Type,
            Price = t.Price,
            Volume = (int)t.Volume
        }).ToList();
    }
}

/// <summary>
/// 散户量化订单结果
/// </summary>
public class SanhuOrderResult
{
    public int Ret { get; set; }
    public string Msg { get; set; } = "";
    public long OrderId { get; set; }
    public long AgreeId { get; set; }
    public string? Time { get; set; }
    public string? Type { get; set; }
    public string? Code { get; set; }
    public int Price { get; set; }
    public int Hands { get; set; }
    public string? Policy { get; set; }

    public bool IsAccepted => Ret == 100;
    public bool IsCompleted => Ret == 212;
    public bool IsFailed => Ret >= 300 || Ret == 201 || Ret == 213;
    public bool IsPending => Ret is >= 100 and < 200;
}

/// <summary>
/// 散户量化委托
/// </summary>
public class SanhuEntrustment
{
    public long AgreeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Price { get; set; }
    public long Volume { get; set; }
}

/// <summary>
/// 散户量化成交
/// </summary>
public class SanhuTrade
{
    public long AgreeId { get; set; }
    public string? Time { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Price { get; set; }
    public long Volume { get; set; }
}
