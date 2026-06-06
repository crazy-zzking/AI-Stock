using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private const string CapitalFlowUrl = "https://push2his.eastmoney.com/api/qt/stock/fflow/daykline/get";
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
        DataCapability.CapitalFlow,
        DataCapability.SectorRanking,
        DataCapability.SectorConstituents,
        DataCapability.StockSectors
    };

    /// <summary>
    /// 获取实时行情
    /// </summary>
    public override async Task<QuoteData?> GetQuoteAsync(string code)
    {
        try
        {
            var secid = GetMarketCode(code);
            var url = $"{QuoteUrl}?secid={secid}&ut={UserToken}&fields=f43,f44,f45,f46,f47,f48,f50,f51,f52,f55,f57,f58,f60,f116,f117,f168,f170,f162,f167";
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
                ChangeAmount = GetDecimal(data.GetProperty("f43"), 100) - GetDecimal(data.GetProperty("f60"), 100),
                TurnoverRate = data.TryGetProperty("f168", out var turnEl) ? GetDecimal(turnEl, 100) : 0,
                VolumeRatio = GetDecimal(data.GetProperty("f50"), 100),
                // 估值字段（东财 stock/get）。已联调校准 ：
                // f116 总市值=元(不除)、f117 流通市值=元(不除)、f162 PE(动)÷100、f167 PB÷100；取不到则为 0。
                TotalMarketCap = data.TryGetProperty("f116", out var mcEl) ? GetDecimal(mcEl) : 0,
                FloatMarketCap = data.TryGetProperty("f117", out var fmcEl) ? GetDecimal(fmcEl) : 0,
                PeTtm = data.TryGetProperty("f162", out var peEl) ? GetDecimal(peEl, 100) : 0,
                Pb = data.TryGetProperty("f167", out var pbEl) ? GetDecimal(pbEl, 100) : 0,
                Timestamp = DateTime.Now,
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
    /// 获取指数日K（按 secid 直传，如上证综指 "1.000001"、深证成指 "0.399001"、创业板指 "0.399006"）。
    /// 指数代码与个股代码前缀规则不同，不能走 GetMarketCode，需显式传 secid。
    /// </summary>
    public async Task<List<KlineData>> GetIndexDailyAsync(string secid, int count = 30, bool fullHistory = false, CancellationToken ct = default)
    {
        try
        {
            // 全量须带 beg=0（纯 lmt 东财有上限拿不全）；增量按 end+lmt 取最近 count 根。
            // 全量取大 lmt 即可拿全部历史日K；不能带 smplmt（会把数据下采样到约 460 点）
            var range = fullHistory
                ? "end=20500101&lmt=100000"
                : $"end=20500101&lmt={count}";
            var url = $"{KlineUrl}?fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&ut={UserToken}&rtntype=6&secid={secid}&klt=101&fqt=1&{range}";
            var response = await SendEastmoneyRequestAsync(url, ct);
            if (response == null) return new List<KlineData>();

            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return new List<KlineData>();

            var result = new List<KlineData>();
            foreach (var item in data.GetProperty("klines").EnumerateArray())
            {
                var p = item.GetString()?.Split(',');
                if (p == null || p.Length < 3) continue;
                result.Add(new KlineData
                {
                    Code = secid,
                    DateTime = DateTime.Parse(p[0]),
                    Open = decimal.Parse(p[1]),
                    Close = decimal.Parse(p[2]),
                    High = p.Length > 3 ? decimal.Parse(p[3]) : 0,
                    Low = p.Length > 4 ? decimal.Parse(p[4]) : 0,
                    ChangePercent = p.Length > 8 ? decimal.Parse(p[8]) : 0,
                    Source = ProviderId
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get index klines for {Secid}", secid);
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
            var url = $"{CapitalFlowUrl}?lmt=0&klt=101&secid={secid}&ut={UserToken}&fields1=f1,f2,f3,f7&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61,f62,f63,f64,f65";
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
                // 东财 fflow klines 顺序：时间, 主力, 小单, 中单, 大单, 超大单
                // （校验：主力 = 大单 + 超大单）
                MainNetInflow = decimal.Parse(parts[1]),
                SmallNetInflow = decimal.Parse(parts[2]),
                MediumNetInflow = decimal.Parse(parts[3]),
                LargeNetInflow = decimal.Parse(parts[4]),
                SuperLargeNetInflow = decimal.Parse(parts[5]),
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
    /// 获取历史每日资金流（fflow/daykline lmt=0 返回全历史；解析全部 klines，供回放回测补资金面）。
    /// </summary>
    public async Task<List<CapitalFlowData>> GetCapitalFlowHistoryAsync(string code, CancellationToken ct = default)
    {
        var result = new List<CapitalFlowData>();
        try
        {
            var secid = GetMarketCode(code);
            var url = $"{CapitalFlowUrl}?lmt=0&klt=101&secid={secid}&ut={UserToken}&fields1=f1,f2,f3,f7&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61,f62,f63,f64,f65";
            var response = await SendEastmoneyRequestAsync(url);
            if (response == null) return result;

            using var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return result;

            foreach (var item in data.GetProperty("klines").EnumerateArray())
            {
                var parts = item.GetString()?.Split(',');
                if (parts == null || parts.Length < 6) continue;
                if (!DateTime.TryParse(parts[0], out var date)) continue;
                if (!decimal.TryParse(parts[1], out var main)) continue;
                result.Add(new CapitalFlowData
                {
                    Code = code,
                    Date = date,
                    // 顺序：时间, 主力, 小单, 中单, 大单, 超大单
                    MainNetInflow = main,
                    SmallNetInflow = Parse(parts, 2),
                    MediumNetInflow = Parse(parts, 3),
                    LargeNetInflow = Parse(parts, 4),
                    SuperLargeNetInflow = Parse(parts, 5),
                    Source = ProviderId,
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "获取历史资金流失败 {Code}", code);
        }
        return result;

        static decimal Parse(string[] p, int i) => i < p.Length && decimal.TryParse(p[i], out var v) ? v : 0m;
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

    // ---- 板块能力 ----

    private const string SectorRankingUrl = "https://push2.eastmoney.com/api/qt/clist/get";
    private const string SectorConstituentsUrl = "https://push2dycalc.eastmoney.com/api/qt/clist/get";
    private const string StockSectorsUrl = "https://datacenter.eastmoney.com/securities/api/data/v1/get";

    /// <summary>
    /// 获取板块排名（资金流向）。outflow=false：主力净流入(f62)降序（流入榜）；
    /// outflow=true：按涨跌幅(f3)升序(po=0) 取弱势/流出板块（独立查询，非流入榜尾部切片）。
    /// fltt=2 数值为直接值（不除 100）。
    /// </summary>
    public async Task<List<SectorFlowData>> GetSectorRankingAsync(bool outflow = false, CancellationToken ct = default)
    {
        var (fid, po) = outflow ? ("f3", 0) : ("f62", 1);
        var url = SectorRankingUrl + "?" +
                  $"fid={fid}&po={po}&pz=50&pn=1&np=1&fltt=2&invt=2&" +
                  "fs=m:90+t:3&" +
                  "fields=f12,f14,f2,f3,f6,f62,f184,f66,f72,f78,f84&" +
                  "ut=8dec03ba335b81bf4ebdf7b29ec27d15";

        try
        {
            var response = await SendEastmoneyRequestAsync(url, ct);
            if (response == null) return new List<SectorFlowData>();
            return ParseSectorRanking(response);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to fetch sector ranking");
            return new List<SectorFlowData>();
        }
    }

    /// <summary>
    /// 获取板块内个股资金流排行（实时，按主力净流入 f62 服务端降序）。
    /// fs=b:{板块代码}，fltt=2 数值为直接值（涨幅 % / 净额元，不除 100）。
    /// </summary>
    public async Task<List<SectorStockFlow>> GetSectorStockFlowAsync(string sectorCode, int top = 10, CancellationToken ct = default)
    {
        var pz = Math.Clamp(top, 1, 50);
        var url = SectorRankingUrl + "?" +
                  $"np=1&fltt=2&invt=2&fs=b:{sectorCode}&fid=f62&pn=1&pz={pz}&po=1&" +
                  "fields=f12,f14,f2,f3,f62,f184,f66,f72,f78,f84&" +
                  "ut=fa5fd1943c7b386f172d6893dbfba10b";

        try
        {
            var response = await SendEastmoneyRequestAsync(url, ct);
            if (response == null) return new List<SectorStockFlow>();
            return ParseSectorStockFlow(response);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to fetch stock flow for sector {SectorCode}", sectorCode);
            return new List<SectorStockFlow>();
        }
    }

    private List<SectorStockFlow> ParseSectorStockFlow(string json)
    {
        var result = new List<SectorStockFlow>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind == JsonValueKind.Null ||
                !data.TryGetProperty("diff", out var diff) ||
                diff.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in diff.EnumerateArray())
            {
                decimal Num(string f) =>
                    item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0;
                string Str(string f) =>
                    item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

                result.Add(new SectorStockFlow
                {
                    Code = Str("f12"),
                    Name = Str("f14"),
                    Price = Num("f2"),
                    ChangePercent = Num("f3"),
                    MainNetInflow = Num("f62"),
                    MainNetRatio = Num("f184"),
                    SuperLargeOrderNet = Num("f66"),
                    LargeOrderNet = Num("f72"),
                    MediumOrderNet = Num("f78"),
                    SmallOrderNet = Num("f84"),
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse sector stock flow");
        }
        return result;
    }

    /// <summary>
    /// 全市场个股主力净流入（批量，clist 分页）。返回 code → 主力净流入(元)，用于盘中快照资金面。
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetMarketMainFlowAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, decimal>();
        const string fs = "m:0+t:6,m:0+t:80,m:1+t:2,m:1+t:23,m:0+t:81+s:2048"; // 沪深京 A 股
        for (int pn = 1; pn <= 60; pn++)
        {
            if (ct.IsCancellationRequested) break;
            var url = SectorRankingUrl + "?" +
                      $"fid=f62&po=1&pz=200&pn={pn}&np=1&fltt=2&invt=2&fs={fs}&fields=f12,f62&" +
                      "ut=8dec03ba335b81bf4ebdf7b29ec27d15";
            var resp = await SendEastmoneyRequestAsync(url, ct);
            if (resp == null) break;

            int count = 0;
            try
            {
                using var doc = JsonDocument.Parse(resp);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind == JsonValueKind.Null ||
                    !data.TryGetProperty("diff", out var diff) ||
                    diff.ValueKind != JsonValueKind.Array)
                    break;

                foreach (var item in diff.EnumerateArray())
                {
                    var code = item.TryGetProperty("f12", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    if (string.IsNullOrEmpty(code)) continue;
                    var flow = item.TryGetProperty("f62", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetDecimal() : 0;
                    result[code] = flow;
                    count++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "解析市场资金流分页失败 pn={Pn}", pn);
                break;
            }
            if (count < 200) break; // 不足一页 = 最后一页
        }
        return result;
    }

    /// <summary>
    /// 获取板块成分股
    /// </summary>
    public async Task<List<string>> GetSectorConstituentsAsync(string sectorCode, CancellationToken ct = default)
    {
        var fs = $"m:1+t:2+b:{sectorCode},m:1+t:23+b:{sectorCode},m:0+t:6+b:{sectorCode},m:0+t:13+b:{sectorCode},m:0+t:80+b:{sectorCode},m:0+t:81+s:2048+b:{sectorCode}";
        var url = SectorConstituentsUrl + "?" +
                  $"fs={fs}&fltt=2&fields=f12,f14&fid=f3&po=1&pn=1&pz=100&np=1&ut={UserToken}";

        try
        {
            var response = await SendEastmoneyRequestAsync(url, ct);
            if (response == null) return new List<string>();

            var result = new List<string>();
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("diff", out var diff))
            {
                foreach (var item in diff.EnumerateArray())
                {
                    if (item.TryGetProperty("f12", out var code))
                        result.Add(code.GetString() ?? "");
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to fetch constituents for {SectorCode}", sectorCode);
            return new List<string>();
        }
    }

    /// <summary>
    /// 获取个股所属板块
    /// </summary>
    public async Task<List<string>> GetStockSectorsAsync(string stockCode, CancellationToken ct = default)
    {
        var secucode = stockCode.ToUpper();
        if (!secucode.Contains("."))
        {
            var marketCode = GetMarketCode(stockCode);
            var prefix = marketCode.Split('.')[0];
            var code = marketCode.Split('.')[1];
            secucode = $"{code}.{(prefix == "1" ? "SH" : "SZ")}";
        }

        var url = StockSectorsUrl + "?" +
                  "reportName=RPT_F10_CORETHEME_BOARDTYPE&" +
                  "columns=SECUCODE,SECURITY_CODE,SECURITY_NAME_ABBR,NEW_BOARD_CODE,BOARD_NAME&" +
                  $"filter=(SECUCODE=\"{secucode}\")(IS_PRECISE=\"1\")&" +
                  "pageNumber=1&pageSize=10&source=HSF10&client=PC";

        try
        {
            var response = await SendEastmoneyRequestAsync(url, ct);
            if (response == null) return new List<string>();

            var result = new List<string>();
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("result", out var res) &&
                res.TryGetProperty("data", out var dataArray))
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("BOARD_NAME", out var boardName))
                    {
                        var name = boardName.GetString();
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch sectors for {StockCode}", stockCode);
            return new List<string>();
        }
    }

    private List<SectorFlowData> ParseSectorRanking(string json)
    {
        var result = new List<SectorFlowData>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind == JsonValueKind.Null ||
                !data.TryGetProperty("diff", out var diff) ||
                diff.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in diff.EnumerateArray())
            {
                decimal Num(string f) =>
                    item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0;
                string Str(string f) =>
                    item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

                // fltt=2：数值均为直接值（涨幅 % / 净流入元，不除 100）
                result.Add(new SectorFlowData
                {
                    SectorCode = Str("f12"),
                    SectorName = Str("f14"),
                    Price = Num("f2"),
                    ChangePercent = Num("f3"),
                    NetInflow = Num("f62"),          // 主力净流入（元）
                    SuperLargeOrderNet = Num("f66"),
                    LargeOrderNet = Num("f72"),
                    MediumOrderNet = Num("f78"),
                    SmallOrderNet = Num("f84"),
                    TurnoverAmount = Num("f6"),
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse sector ranking");
        }
        return result;
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
            // 精简日志：超时/取消等多为瞬时网络故障，只打一行原因，不展开整条嵌套异常堆栈
            Logger.LogWarning("Eastmoney 请求失败: {Url} — {Error}", url, ex.Message);
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

/// <summary>
/// 板块排名接口响应
/// </summary>
internal class SectorRankingResponse
{
    [JsonPropertyName("data")]
    public SectorRankingData? Data { get; set; }
}

internal class SectorRankingData
{
    [JsonPropertyName("diff")]
    public List<SectorItem> Diff { get; set; } = new();
}

internal class SectorItem
{
    [JsonPropertyName("f3")] public decimal F3 { get; set; }
    [JsonPropertyName("f12")] public string F12 { get; set; } = "";
    [JsonPropertyName("f14")] public string F14 { get; set; } = "";
    [JsonPropertyName("f621")] public decimal F621 { get; set; }
}