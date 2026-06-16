using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIStock.Monitor;

/// <summary>告警级别</summary>
public enum AlertLevel
{
    Info,
    Warning,
    Critical
}

/// <summary>一条告警</summary>
public record Alert(AlertLevel Level, string Title, string Message);

/// <summary>告警通知接口</summary>
public interface IAlertNotifier
{
    Task SendAsync(Alert alert, CancellationToken ct = default);
}

/// <summary>
/// 日志告警 — 始终可用，按级别写日志。其他通知器也应同时写日志，避免告警丢失。
/// </summary>
public class LogAlertNotifier : IAlertNotifier
{
    private readonly ILogger<LogAlertNotifier> _logger;

    public LogAlertNotifier(ILogger<LogAlertNotifier> logger) => _logger = logger;

    public Task SendAsync(Alert alert, CancellationToken ct = default)
    {
        var level = alert.Level switch
        {
            AlertLevel.Critical => LogLevel.Error,
            AlertLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        _logger.Log(level, "[告警/{Level}] {Title} — {Message}", alert.Level, alert.Title, alert.Message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Webhook 告警 — 兼容企业微信/钉钉群机器人的 text 消息格式；同时写日志。
/// </summary>
public class WebhookAlertNotifier : IAlertNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookAlertNotifier> _logger;
    private readonly string _webhookUrl;

    public WebhookAlertNotifier(IHttpClientFactory httpClientFactory, ILogger<WebhookAlertNotifier> logger, string webhookUrl)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _webhookUrl = webhookUrl;
    }

    public async Task SendAsync(Alert alert, CancellationToken ct = default)
    {
        // 先落日志，确保即使推送失败也有记录
        var level = alert.Level switch
        {
            AlertLevel.Critical => LogLevel.Error,
            AlertLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        _logger.Log(level, "[告警/{Level}] {Title} — {Message}", alert.Level, alert.Title, alert.Message);

        try
        {
            var content = $"【{alert.Level}】{alert.Title}\n{alert.Message}";
            var payload = JsonSerializer.Serialize(new { msgtype = "text", text = new { content } });
            var client = _httpClientFactory.CreateClient("default");
            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(_webhookUrl, body, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("告警 Webhook 推送失败：HTTP {Status}", (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "告警 Webhook 推送异常");
        }
    }
}
