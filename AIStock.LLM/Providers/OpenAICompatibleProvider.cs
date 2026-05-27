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
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds
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
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
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
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
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
