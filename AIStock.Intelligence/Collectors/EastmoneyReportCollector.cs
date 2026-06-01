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

                                if (item.TryGetProperty("stockList", out var stockList))
                                {
                                    foreach (var stock in stockList.EnumerateArray())
                                    {
                                        var code = stock.TryGetProperty("stockCode", out var codeEl) ? codeEl.GetString() : null;
                                        if (!string.IsNullOrEmpty(code))
                                        {
                                            report.RelatedStocks.Add(code);
                                        }
                                    }
                                }

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
