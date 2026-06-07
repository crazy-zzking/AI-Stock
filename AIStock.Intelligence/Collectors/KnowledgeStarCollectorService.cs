using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Intelligence.Collectors;

/// <summary>
/// 知识星球采集器。两种方式：
/// 1) UseCli=true（推荐）：调官方 zsxq-cli（OAuth 密钥）取 JSON，绕开签名/401；
/// 2) UseCli=false：旧的 HTTP + access_token cookie 抓取（已易被 401，作回退）。
/// 防封：请求前随机抖动、保守频率（调度层控制低频）。
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

    public Task<List<KnowledgeStarContent>> GetGroupContentAsync(string groupId, int count = 20, CancellationToken cancellationToken = default)
        => _options.UseCli
            ? GetGroupContentViaCliAsync(groupId, count, cancellationToken)
            : GetGroupContentViaHttpAsync(groupId, count, cancellationToken);

    /// <summary>
    /// 经官方 zsxq-cli 取最新主题（OAuth 密钥，需宿主已 auth login）。
    /// </summary>
    private async Task<List<KnowledgeStarContent>> GetGroupContentViaCliAsync(string groupId, int count, CancellationToken cancellationToken)
    {
        var result = new List<KnowledgeStarContent>();
        try
        {
            await RandomDelayAsync(cancellationToken);

            // zsxq-cli group +topics --group-id <id> --limit <1..30> --json
            var limit = Math.Clamp(count, 1, 30);
            var args = $"group +topics --group-id {groupId} --limit {limit} --json";
            var stdout = await RunCliAsync(args, cancellationToken);
            if (string.IsNullOrWhiteSpace(stdout))
            {
                _logger.LogWarning("知识星球 CLI 无输出 group={Group}（可能未安装 zsxq-cli 或未 auth login）", groupId);
                return result;
            }

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                _logger.LogWarning("知识星球 CLI 返回 success=false group={Group}", groupId);
                return result;
            }
            if (!root.TryGetProperty("topics_brief", out var topics) || topics.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var topic in topics.EnumerateArray())
            {
                try
                {
                    result.Add(ParseCliTopic(topic));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "解析星球主题失败(CLI)");
                }
            }

            _logger.LogInformation("知识星球(CLI) group={Group} 采集 {Count} 条", groupId, result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "知识星球 CLI 采集异常 group={Group}", groupId);
        }
        return result;
    }

    private async Task<List<KnowledgeStarContent>> GetGroupContentViaHttpAsync(string groupId, int count, CancellationToken cancellationToken)
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
                {
                    // 优先取原图（OCR 准确率更高）；缺失时回退 large / thumbnail，避免漏图
                    var url = PickImageUrl(img, "original") ?? PickImageUrl(img, "large") ?? PickImageUrl(img, "thumbnail");
                    if (!string.IsNullOrEmpty(url))
                        imageUrls.Add(url);
                }
            }
        }
        else if (topic.TryGetProperty("question", out var q) && q.TryGetProperty("text", out var qt))
        {
            text = qt.GetString() ?? "";
        }

        var createTime = topic.TryGetProperty("create_time", out var ct2) && DateTime.TryParse(ct2.GetString(), out var dt)
            ? dt
            : DateTime.Now;

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

    /// <summary>
    /// 解析 zsxq-cli 的 topics_brief 项（结构较扁：content / owner.name / images[] 同级）。
    /// </summary>
    private static KnowledgeStarContent ParseCliTopic(JsonElement topic)
    {
        var topicId = topic.TryGetProperty("topic_id", out var idEl)
            ? (idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString() : idEl.GetString() ?? "")
            : "";

        var text = topic.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

        var author = "";
        if (topic.TryGetProperty("owner", out var owner) && owner.TryGetProperty("name", out var n))
            author = n.GetString() ?? "";

        var imageUrls = new List<string>();
        if (topic.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var img in imgs.EnumerateArray())
            {
                // 优先取原图（OCR 准确率更高）；缺失时回退 large / thumbnail，避免漏图
                var url = PickImageUrl(img, "original") ?? PickImageUrl(img, "large") ?? PickImageUrl(img, "thumbnail");
                if (!string.IsNullOrEmpty(url))
                    imageUrls.Add(url);
            }
        }

        var createTime = topic.TryGetProperty("create_time", out var ct2) && DateTime.TryParse(ct2.GetString(), out var dt)
            ? dt
            : DateTime.Now;

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

    /// <summary>
    /// 从单个 image 对象按指定尺寸字段取 url（original/large/thumbnail）
    /// </summary>
    private static string? PickImageUrl(JsonElement img, string size)
    {
        if (img.TryGetProperty(size, out var node) &&
            node.TryGetProperty("url", out var u) &&
            u.ValueKind == JsonValueKind.String)
        {
            var url = u.GetString();
            return string.IsNullOrEmpty(url) ? null : url;
        }
        return null;
    }

    /// <summary>
    /// 调用 zsxq-cli 并返回 stdout。Windows 经 cmd /c（解析 .cmd 包装），其它平台直接执行。
    /// </summary>
    private async Task<string?> RunCliAsync(string arguments, CancellationToken ct)
    {
        var isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (isWin)
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c {_options.CliPath} {arguments}";
        }
        else
        {
            psi.FileName = _options.CliPath;
            psi.Arguments = arguments;
        }

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start())
                return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动 zsxq-cli 失败（CliPath={Path}）", _options.CliPath);
            return null;
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.CliTimeoutSeconds)));
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("zsxq-cli 超时（{Sec}s），已终止", _options.CliTimeoutSeconds);
            try { proc.Kill(true); } catch { /* ignore */ }
            return null;
        }

        var stdout = await stdoutTask;
        if (proc.ExitCode != 0)
        {
            var stderr = await stderrTask;
            _logger.LogWarning("zsxq-cli 非零退出 code={Code}: {Err}", proc.ExitCode,
                string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            return null;
        }
        return stdout;
    }

    private async Task RandomDelayAsync(CancellationToken ct)
    {
        var min = Math.Max(0, _options.MinDelayMs);
        var max = Math.Max(min + 1, _options.MaxDelayMs);
        var delay = _rand.Next(min, max);
        await Task.Delay(delay, ct);
    }
}
