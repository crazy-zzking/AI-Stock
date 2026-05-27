using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Intelligence.Common;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AIStock.Intelligence.Collectors;

/// <summary>
/// 新闻采集器
/// </summary>
public class NewsCollectorService : INewsCollector
{
    private readonly PlaywrightHelper _playwrightHelper;
    private readonly ILogger<NewsCollectorService> _logger;

    public string CollectorId => "news-collector";

    public NewsCollectorService(
        PlaywrightHelper playwrightHelper,
        ILogger<NewsCollectorService> logger)
    {
        _playwrightHelper = playwrightHelper;
        _logger = logger;
    }

    public async Task<List<NewsData>> CollectLatestNewsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        return await _playwrightHelper.ExecuteWithBrowserAsync(async page =>
        {
            var newsList = new List<NewsData>();

            try
            {
                // 使用东方财富公告API
                var url = $"https://np-anotice-stock.eastmoney.com/api/security/ann?sr=-1&page_size={count}&page_index=1&ann_type=SHA&client_source=web&f_node=0";
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });

                if (response != null && response.Ok)
                {
                    var jsonContent = await page.EvaluateAsync<string>("() => document.body.innerText");

                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        try
                        {
                            var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                            var root = jsonDoc.RootElement;

                            if (root.TryGetProperty("data", out var data) &&
                                data.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                if (data.TryGetProperty("list", out var list) &&
                                    list.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var item in list.EnumerateArray())
                                    {
                                        try
                                        {
                                            var title = item.GetProperty("title").GetString() ?? "";
                                            var stockCode = "";
                                            if (item.TryGetProperty("codes", out var codes) && codes.GetArrayLength() > 0)
                                            {
                                                stockCode = codes[0].TryGetProperty("stock_code", out var sc) ? sc.GetString() ?? "" : "";
                                            }

                                            var news = new NewsData
                                            {
                                                Title = title,
                                                Source = "东方财富",
                                                Url = $"https://data.eastmoney.com/notices/detail/{stockCode}/{item.GetProperty("art_code").GetString()}.html",
                                                PublishTime = item.TryGetProperty("notice_date", out var date)
                                                    ? DateTime.Parse(date.GetString() ?? DateTime.UtcNow.ToString())
                                                    : DateTime.UtcNow,
                                                Category = "公告"
                                            };

                                            if (!string.IsNullOrEmpty(stockCode))
                                            {
                                                news.RelatedStocks.Add(stockCode);
                                            }

                                            newsList.Add(news);
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning(ex, "Failed to parse news item");
                                        }
                                    }
                                }
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse JSON response");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to collect latest news");
            }

            return newsList;
        });
    }

    public async Task<List<NewsData>> CollectNewsByStockAsync(string stockCode, int count = 20, CancellationToken cancellationToken = default)
    {
        return await _playwrightHelper.ExecuteWithBrowserAsync(async page =>
        {
            var newsList = new List<NewsData>();

            try
            {
                var url = $"https://search-api-web.eastmoney.com/search/jsonp?cb=jQuery&param=%7B%22uid%22%3A%22%22%2C%22keyword%22%3A%22{stockCode}%22%2C%22type%22%3A%5B%22cmsArticleWebOld%22%5D%2C%22client%22%3A%22web%22%2C%22clientType%22%3A%22web%22%2C%22clientVersion%22%3A%22curr%22%2C%22param%22%3A%7B%22cmsArticleWebOld%22%3A%7B%22searchScope%22%3A%22default%22%2C%22sort%22%3A%22default%22%2C%22pageIndex%22%3A1%2C%22pageSize%22%3A{count}%2C%22preTag%22%3A%22%3Cem%3E%22%2C%22postTag%22%3A%22%3C%2Fem%3E%22%7D%7D%7D";

                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });

                if (response != null && response.Ok)
                {
                    var jsonContent = await page.EvaluateAsync<string>("() => document.body.innerText");
                    // 解析 JSONP 响应
                    var jsonStart = jsonContent.IndexOf('(') + 1;
                    var jsonEnd = jsonContent.LastIndexOf(')');
                    if (jsonStart > 0 && jsonEnd > jsonStart)
                    {
                        var json = jsonContent[jsonStart..jsonEnd];
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                        var root = jsonDoc.RootElement;

                        if (root.TryGetProperty("result", out var result) &&
                            result.TryGetProperty("cmsArticleWebOld", out var articles))
                        {
                            foreach (var item in articles.EnumerateArray())
                            {
                                try
                                {
                                    var news = new NewsData
                                    {
                                        Title = item.GetProperty("title").GetString() ?? "",
                                        Source = item.TryGetProperty("mediaName", out var source) ? source.GetString() ?? "" : "",
                                        Url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "",
                                        PublishTime = item.TryGetProperty("date", out var date)
                                            ? DateTime.Parse(date.GetString() ?? DateTime.UtcNow.ToString())
                                            : DateTime.UtcNow,
                                        Summary = item.TryGetProperty("content", out var content) ? content.GetString() : null
                                    };

                                    news.RelatedStocks.Add(stockCode);
                                    newsList.Add(news);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse stock news item");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to collect news for stock {StockCode}", stockCode);
            }

            return newsList;
        });
    }

    public async Task<List<NewsData>> CollectNewsByCategoryAsync(string category, int count = 50, CancellationToken cancellationToken = default)
    {
        // 根据类别选择不同的栏目
        var columnMap = new Dictionary<string, string>
        {
            ["财经"] = "350,35,469",
            ["政策"] = "351,362",
            ["公司"] = "352,363",
            ["行业"] = "353,364"
        };

        var columns = columnMap.TryGetValue(category, out var col) ? col : "350,35,469";

        return await _playwrightHelper.ExecuteWithBrowserAsync(async page =>
        {
            var newsList = new List<NewsData>();

            try
            {
                var url = $"https://np-listapi.eastmoney.com/comm/web/getNewsByColumns?client=web&columns={columns}&pageSize={count}&pageNo=1";
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });

                if (response != null && response.Ok)
                {
                    var jsonContent = await page.EvaluateAsync<string>("() => document.body.innerText");

                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        try
                        {
                            var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                            var root = jsonDoc.RootElement;

                            if (root.TryGetProperty("data", out var data) &&
                                data.ValueKind != System.Text.Json.JsonValueKind.Null &&
                                data.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                if (data.TryGetProperty("list", out var list) &&
                                    list.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var item in list.EnumerateArray())
                                    {
                                        try
                                        {
                                            var news = new NewsData
                                            {
                                                Title = item.GetProperty("title").GetString() ?? "",
                                                Source = item.TryGetProperty("source", out var source) ? source.GetString() ?? "东方财富" : "东方财富",
                                                Url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "",
                                                PublishTime = item.TryGetProperty("showTime", out var time)
                                                    ? DateTime.Parse(time.GetString() ?? DateTime.UtcNow.ToString())
                                                    : DateTime.UtcNow,
                                                Summary = item.TryGetProperty("digest", out var digest) ? digest.GetString() : null,
                                                Category = category
                                            };

                                            newsList.Add(news);
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning(ex, "Failed to parse news item");
                                        }
                                    }
                                }
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse JSON response");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to collect news for category {Category}", category);
            }

            return newsList;
        });
    }
}
