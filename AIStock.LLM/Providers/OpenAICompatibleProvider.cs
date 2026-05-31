using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.LLM.Providers;

/// <summary>
/// OpenAI兼容的LLM提供者
/// </summary>
public class OpenAICompatibleProvider : ILLMProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAICompatibleProvider> _logger;

    // 全局并发限制：最多 5 个 LLM 请求同时进行，防止打爆 API 配额
    private static readonly SemaphoreSlim _concurrencyLimiter = new(5, 5);

    private const int MaxRetries = 3;

    public string ProviderId => "openai-compatible";

    public OpenAICompatibleProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAICompatibleProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<LLMResponse> SendAsync(LLMConfig config, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            return await SendWithRetryAsync(config, request, stopwatch, cancellationToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private async Task<LLMResponse> SendWithRetryAsync(LLMConfig config, LLMRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var result = await SendOnceAsync(config, request, cancellationToken);
                if (result.Success || attempt == MaxRetries)
                {
                    result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                    return result;
                }

                // 429 或 5xx 触发重试
                if (result.ErrorMessage?.Contains("429") == true ||
                    result.ErrorMessage?.Contains("5") == true)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s
                    _logger.LogWarning("LLM request failed (attempt {Attempt}/{Max}), retrying in {Delay}s: {Error}",
                        attempt, MaxRetries, delay.TotalSeconds, result.ErrorMessage);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex, "LLM request exception (attempt {Attempt}/{Max}), retrying in {Delay}s",
                    attempt, MaxRetries, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return new LLMResponse
        {
            Success = false,
            ErrorMessage = "Max retries exceeded",
            ModelId = config.Id,
            ModelName = config.Name,
            ResponseTimeMs = stopwatch.ElapsedMilliseconds
        };
    }

    private async Task<LLMResponse> SendOnceAsync(LLMConfig config, LLMRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            var requestBody = BuildRequestBody(config, request, stream: false);
            var requestBytes = Encoding.UTF8.GetBytes(requestBody);
            var content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

            var url = $"{config.BaseUrl.TrimEnd('/')}/chat/completions";
            var httpResponse = await client.PostAsync(url, content, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("LLM API error: {StatusCode} - {Response}", httpResponse.StatusCode, responseJson);
                return new LLMResponse
                {
                    Success = false,
                    ErrorMessage = $"API error: {httpResponse.StatusCode} - {responseJson}",
                    ModelId = config.Id,
                    ModelName = config.Name,
                    ResponseTimeMs = 0 // 由调用方覆盖
                };
            }

            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
            var choices = responseObj.GetProperty("choices");
            var firstChoice = choices[0];
            var message = firstChoice.GetProperty("message");
            var responseContent = message.GetProperty("content").GetString() ?? string.Empty;

            TokenUsage? usage = null;
            if (responseObj.TryGetProperty("usage", out var usageElement))
            {
                usage = new TokenUsage
                {
                    PromptTokens = usageElement.GetProperty("prompt_tokens").GetInt32(),
                    CompletionTokens = usageElement.GetProperty("completion_tokens").GetInt32(),
                    TotalTokens = usageElement.GetProperty("total_tokens").GetInt32()
                };
            }

            return new LLMResponse
            {
                Success = true,
                Content = responseContent,
                ModelId = config.Id,
                ModelName = config.Name,
                Usage = usage,
                ResponseTimeMs = 0 // 由调用方覆盖
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM request failed for model {ModelId}", config.Id);
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ModelId = config.Id,
                ModelName = config.Name,
                ResponseTimeMs = 0
            };
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        LLMConfig config,
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        var requestBody = BuildRequestBody(config, request, stream: true);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

        var url = $"{config.BaseUrl.TrimEnd('/')}/chat/completions";

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.PostAsync(url, content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM stream request failed for model {ModelId}", config.Id);
            yield break;
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("LLM stream API error: {StatusCode} - {Response}", httpResponse.StatusCode, errorBody);
            yield break;
        }

        var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..].Trim();
            if (data == "[DONE]") break;

            JsonElement chunkObj;
            try
            {
                chunkObj = JsonSerializer.Deserialize<JsonElement>(data);
            }
            catch
            {
                continue;
            }

            if (chunkObj.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var contentElement))
                {
                    var text = contentElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }
            }
        }
    }

    private static string BuildRequestBody(LLMConfig config, LLMRequest request, bool stream)
    {
        var messages = new List<object>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }
        messages.Add(new { role = "user", content = request.UserPrompt });

        var body = new Dictionary<string, object>
        {
            ["model"] = config.Model,
            ["messages"] = messages,
            ["stream"] = stream
        };

        var maxTokens = request.MaxTokens ?? config.MaxTokens;
        if (maxTokens.HasValue)
        {
            body["max_tokens"] = maxTokens.Value;
        }

        var temperature = request.Temperature ?? config.Temperature;
        if (temperature.HasValue)
        {
            body["temperature"] = (double)temperature.Value;
        }

        return JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
