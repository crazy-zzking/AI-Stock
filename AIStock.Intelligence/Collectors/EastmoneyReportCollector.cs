using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;

namespace AIStock.Intelligence.Collectors;

/// <summary>
/// 东方财富研报采集器（使用HttpClient）
/// </summary>
public class EastmoneyReportCollector : IReportCollector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EastmoneyReportCollector> _logger;

    public string CollectorId => "eastmoney-report";

    public EastmoneyReportCollector(
        IHttpClientFactory httpClientFactory,
        ILogger<EastmoneyReportCollector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<ReportData>> CollectReportsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        var reports = new List<ReportData>();

        try
        {
            var client = _httpClientFactory.CreateClient("eastmoney");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Add("Referer", "https://data.eastmoney.com/report/stock.jshtml");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");

            var beginTime = DateTime.Now.AddMonths(-24).ToString("yyyy-MM-dd");
            var endTime = DateTime.Now.ToString("yyyy-MM-dd");

            var requestBody = new
            {
                beginTime,
                endTime,
                industryCode = "*",
                ratingChange = "*",
                rating = "*",
                orgCode = (string?)null,
                code = "*",
                rcode = "",
                pageSize = count,
                pageNo = 1
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync("https://reportapi.eastmoney.com/report/list2", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var jsonContent = System.Text.Encoding.UTF8.GetString(responseBytes);

                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonContent);

                    if (jsonDoc.TryGetProperty("data", out var dataArray) &&
                        dataArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataArray.EnumerateArray())
                        {
                            try
                            {
                                var report = new ReportData
                                {
                                    Title = item.GetProperty("title").GetString() ?? "",
                                    Source = "东方财富",
                                    Author = item.TryGetProperty("orgName", out var org) ? org.GetString() : null,
                                    PublishTime = item.TryGetProperty("publishDate", out var date)
                                        ? DateTime.Parse(date.GetString() ?? DateTime.Now.ToString())
                                        : DateTime.Now,
                                    Url = $"https://data.eastmoney.com/report/info/{item.GetProperty("infoCode").GetString()}.html",
                                    ReportType = item.TryGetProperty("type", out var type) ? type.GetString() : null,
                                    Rating = item.TryGetProperty("emRatingName", out var rating) ? rating.GetString() : null,
                                    Summary = item.TryGetProperty("title", out var title) ? title.GetString() : null
                                };

                                // list2 接口把关联个股平铺在 item 上（stockCode），无 stockList 数组
                                if (item.TryGetProperty("stockCode", out var codeEl))
                                {
                                    var code = codeEl.GetString();
                                    if (!string.IsNullOrEmpty(code))
                                    {
                                        report.RelatedStocks.Add(code);
                                    }
                                }

                                if (item.TryGetProperty("indvAimPriceT", out var aimEl) &&
                                    decimal.TryParse(aimEl.GetString(), out var aimPrice) &&
                                    aimPrice > 0)
                                {
                                    report.TargetPrice = aimPrice;
                                }

                                // 把接口里的结构化干货拼成摘要，供下游 LLM 抽取/总结
                                report.Content = BuildDigest(item, report.TargetPrice);

                                reports.Add(report);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse report item");
                            }
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch reports: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect reports from eastmoney");
        }

        return reports;
    }

    /// <summary>
    /// 从 list2 单条研报的结构化字段拼出中文摘要，作为正文供 LLM 总结/抽取。
    /// </summary>
    private static string BuildDigest(JsonElement item, decimal? targetPrice)
    {
        string? Str(string name) =>
            item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() : null;

        var lines = new List<string>();

        var stockName = Str("stockName");
        var stockCode = Str("stockCode");
        if (!string.IsNullOrEmpty(stockName) || !string.IsNullOrEmpty(stockCode))
            lines.Add($"关联个股：{stockName}（{stockCode}）");

        var industry = Str("indvInduName");
        if (!string.IsNullOrEmpty(industry))
            lines.Add($"所属行业：{industry}");

        var org = Str("orgName");
        var researcher = Str("researcher");
        if (!string.IsNullOrEmpty(org))
            lines.Add($"评级机构：{org}" + (string.IsNullOrEmpty(researcher) ? "" : $"（研究员：{researcher}）"));

        var rating = Str("emRatingName");
        if (!string.IsNullOrEmpty(rating))
        {
            var lastRating = Str("lastEmRatingName");
            var change = RatingChange(rating, Str("emRatingValue"), lastRating, Str("lastEmRatingValue"));
            lines.Add($"投资评级：{rating}（{change}）");
        }

        // 盈利预测：今年 / 明年 / 后年 的 EPS 与 PE
        var eps = new List<string>();
        void AddEps(string label, string epsKey, string peKey)
        {
            var e = Str(epsKey);
            var p = Str(peKey);
            var hasE = decimal.TryParse(e, out var ev) && ev != 0;
            var hasP = decimal.TryParse(p, out var pv) && pv != 0;
            if (hasE || hasP)
            {
                var seg = label + "：";
                if (hasE) seg += $"EPS {decimal.Parse(e):0.##}";
                if (hasE && hasP) seg += "，";
                if (hasP) seg += $"PE {decimal.Parse(p):0.##}";
                eps.Add(seg);
            }
        }
        AddEps("今年", "predictThisYearEps", "predictThisYearPe");
        AddEps("明年", "predictNextYearEps", "predictNextYearPe");
        AddEps("后年", "predictNextTwoYearEps", "predictNextTwoYearPe");
        if (eps.Count > 0)
            lines.Add("盈利预测：" + string.Join("；", eps));

        if (targetPrice.HasValue)
            lines.Add($"目标价：{targetPrice.Value:0.##}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 用当前/前次评级判定评动方向（数值码不完整，故按评级名与评级值对比推导）。
    /// </summary>
    private static string RatingChange(string? rating, string? value, string? lastRating, string? lastValue)
    {
        if (string.IsNullOrEmpty(lastRating)) return "首次";
        if (rating == lastRating) return "维持";
        if (int.TryParse(value, out var v) && int.TryParse(lastValue, out var lv))
            return v > lv ? "调高" : "调低";
        return "评级变动";
    }

    public async Task<List<ReportData>> CollectReportsByStockAsync(string stockCode, int count = 20, CancellationToken cancellationToken = default)
    {
        var reports = new List<ReportData>();

        try
        {
            var client = _httpClientFactory.CreateClient("eastmoney");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Add("Referer", "https://data.eastmoney.com/");

            var url = $"https://reportapi.eastmoney.com/report/list?code={stockCode}&pageNo=1&pageSize={count}&fields=&qType=0&orgCode=&rcode=&_={DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
            var response = await client.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonContent);

                    if (jsonDoc.TryGetProperty("data", out var dataArray) &&
                        dataArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataArray.EnumerateArray())
                        {
                            try
                            {
                                var report = new ReportData
                                {
                                    Title = item.GetProperty("title").GetString() ?? "",
                                    Source = "东方财富",
                                    Author = item.TryGetProperty("orgName", out var org) ? org.GetString() : null,
                                    PublishTime = item.TryGetProperty("publishDate", out var date)
                                        ? DateTime.Parse(date.GetString() ?? DateTime.Now.ToString())
                                        : DateTime.Now,
                                    Url = $"https://data.eastmoney.com/report/info/{item.GetProperty("infoCode").GetString()}.html",
                                    ReportType = item.TryGetProperty("type", out var type) ? type.GetString() : null,
                                    Rating = item.TryGetProperty("emRatingName", out var rating) ? rating.GetString() : null
                                };

                                report.RelatedStocks.Add(stockCode);
                                reports.Add(report);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse report item");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect reports for stock {StockCode}", stockCode);
        }

        return reports;
    }

    public async Task<ReportData?> GetReportDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("eastmoney");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await client.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var report = new ReportData
                {
                    Source = "东方财富",
                    Url = url
                };

                var titleNode = doc.DocumentNode.SelectSingleNode("//h1[@class='report-title']");
                if (titleNode != null)
                {
                    report.Title = titleNode.InnerText.Trim();
                }

                var contentNode = doc.DocumentNode.SelectSingleNode("//div[@class='report-content']");
                if (contentNode != null)
                {
                    report.Content = contentNode.InnerText.Trim();
                    report.Summary = report.Content.Length > 500 ? report.Content[..500] + "..." : report.Content;
                }

                return report;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get report detail from {Url}", url);
        }

        return null;
    }
}
