using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Intelligence.Collectors;

/// <summary>
/// 知识星球采集器 — 经 zsxq API + access_token 抓取。
/// 防封：每次请求前随机抖动延迟、真实 UA、保守频率（调度层控制低频）。
/// </summary>
public class KnowledgeStarCollectorService : IKnowledgeStarCollector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KnowledgeStarOptions _options;
    private readonly ILogger<KnowledgeStarCollectorService> _logger;
    private readonly Random _rand = new();

    public string CollectorId => "knowledge-star";

    public KnowledgeStarCollectorService(
        IHttpClientFactory httpClientFactory,
        IOptions<KnowledgeStarOptions> options,
        ILogger<KnowledgeStarCollectorService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<KnowledgeStarContent>> GetLatestContentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var all = new List<KnowledgeStarContent>();
        if (_options.GroupIds.Count == 0)
        {
            _logger.LogWarning("未配置知识星球 GroupIds，跳过");
            return all;
        }
        foreach (var groupId in _options.GroupIds)
        {
            if (cancellationToken.IsCancellationRequested) break;
            all.AddRange(await GetGroupContentAsync(groupId, count, cancellationToken));
        }
        return all;
    }

    public async Task<List<KnowledgeStarContent>> GetGroupContentAsync(string groupId, int count = 20, CancellationToken cancellationToken = default)
    {
        var result = new List<KnowledgeStarContent>();
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            _logger.LogWarning("未配置知识星球 AccessToken，跳过");
            return result;
        }

        try
        {
            // 防封：请求前随机抖动
            await RandomDelayAsync(cancellationToken);

            var client = _httpClientFactory.CreateClient();
            var url = $"{_options.BaseUrl}/groups/{groupId}/topics?scope=all&count={Math.Min(count, _options.Count)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            req.Headers.TryAddWithoutValidation("Cookie", $"zsxq_access_token={_options.AccessToken}");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

            var resp = await client.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("知识星球请求失败 group={Group}: HTTP {Status}", groupId, (int)resp.StatusCode);
                return result;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("succeeded", out var ok) || !ok.GetBoolean())
            {
                _logger.LogWarning("知识星球返回 succeeded=false group={Group}（token 可能失效或被限流）", groupId);
                return result;
            }

            if (!root.TryGetProperty("resp_data", out var respData) ||
                !respData.TryGetProperty("topics", out var topics) ||
                topics.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var topic in topics.EnumerateArray())
            {
                try
                {
                    result.Add(ParseTopic(topic, groupId));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "解析星球主题失败");
                }
            }

            _logger.LogInformation("知识星球 group={Group} 采集 {Count} 条", groupId, result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "知识星球采集异常 group={Group}", groupId);
        }

        return result;
    }

    public Task<List<KnowledgeStarContent>> SearchContentAsync(string keyword, int count = 20, CancellationToken cancellationToken = default)
    {
        // 搜索接口同样需要 token，暂用最新内容做关键字过滤
        return GetLatestContentAsync(count, cancellationToken)
            .ContinueWith(t => t.Result.Where(c => c.Content.Contains(keyword) || c.Title.Contains(keyword)).ToList(), cancellationToken);
    }

    private static KnowledgeStarContent ParseTopic(JsonElement topic, string groupId)
    {
        var topicId = topic.TryGetProperty("topic_id", out var idEl)
            ? (idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString() : idEl.GetString() ?? "")
            : "";

        // 正文可能在 talk.text 或 question.text
        string text = "";
        var imageUrls = new List<string>();
        string author = "";

        if (topic.TryGetProperty("talk", out var talk))
        {
            if (talk.TryGetProperty("text", out var t)) text = t.GetString() ?? "";
            if (talk.TryGetProperty("owner", out var owner) && owner.TryGetProperty("name", out var n))
                author = n.GetString() ?? "";
            if (talk.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in imgs.EnumerateArray())
                    if (img.TryGetProperty("large", out var large) && large.TryGetProperty("url", out var u))
                        imageUrls.Add(u.GetString() ?? "");
            }
        }
        else if (topic.TryGetProperty("question", out var q) && q.TryGetProperty("text", out var qt))
        {
            text = qt.GetString() ?? "";
        }

        var createTime = topic.TryGetProperty("create_time", out var ct2) && DateTime.TryParse(ct2.GetString(), out var dt)
            ? dt
            : DateTime.UtcNow;

        var title = text.Length > 40 ? text[..40] : text;

        return new KnowledgeStarContent
        {
            Title = title,
            Content = text,
            Author = author,
            PublishTime = createTime,
            Url = $"https://wx.zsxq.com/dweb2/index/topic_detail/{topicId}",
            ContentType = imageUrls.Count > 0 ? "image" : "text",
            ImageUrls = imageUrls
        };
    }

    private async Task RandomDelayAsync(CancellationToken ct)
    {
        var min = Math.Max(0, _options.MinDelayMs);
        var max = Math.Max(min + 1, _options.MaxDelayMs);
        var delay = _rand.Next(min, max);
        await Task.Delay(delay, ct);
    }
}
