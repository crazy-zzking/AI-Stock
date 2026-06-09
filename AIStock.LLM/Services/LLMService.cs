using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Prompt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AIStock.LLM.Services;

/// <summary>
/// LLM服务实现
/// </summary>
public class LLMService : ILLMService
{
    private static readonly ActivitySource ActivitySource = new("AIStock.LLM");
    private readonly AIStockDbContext _dbContext;
    private readonly ILLMProvider _llmProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LLMService> _logger;
    private readonly IPromptRegistry? _promptRegistry;
    private readonly IHttpClientFactory _httpClientFactory;

    private const string CacheKey = "LLM_Model_Configs";
    private const string CacheKeyAll = "LLM_Model_Configs_All";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    public LLMService(
        AIStockDbContext dbContext,
        ILLMProvider llmProvider,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        ILogger<LLMService> logger,
        IPromptRegistry? promptRegistry = null)
    {
        _dbContext = dbContext;
        _llmProvider = llmProvider;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _promptRegistry = promptRegistry;
    }

    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var configs = await GetModelConfigsAsync();

        var modelId = request.ModelId ?? GetDefaultModelId(configs);
        if (string.IsNullOrEmpty(modelId))
        {
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = "No available model configured"
            };
        }

        return await SendAsync(request, modelId, cancellationToken);
    }

    public async Task<LLMResponse> SendAsync(LLMRequest request, string modelId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("LLM.Send", ActivityKind.Client);
        activity?.SetTag("llm.model_id", modelId);
        activity?.SetTag("llm.temperature", (double)request.Temperature);

         var configs = await GetModelConfigsAsync();

        if (!configs.TryGetValue(modelId, out var config))
        {
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = $"Model not found: {modelId}"
            };
        }

        if (!config.IsEnabled)
        {
            return new LLMResponse
            {
                Success = false,
                ErrorMessage = $"Model is disabled: {modelId}"
            };
        }

        return await _llmProvider.SendAsync(config, request, cancellationToken);
    }

    public async Task<LLMCompareResult> CompareAsync(LLMRequest request, int modelCount = 3, CancellationToken cancellationToken = default)
    {
        var configs = await GetModelConfigsAsync();

        var enabledModels = configs.Values
            .Where(m => m.IsEnabled)
            .OrderBy(m => m.Priority)
            .Take(modelCount)
            .ToList();

        if (enabledModels.Count == 0)
        {
            return new LLMCompareResult { Success = false };
        }

        var tasks = enabledModels.Select(config =>
            _llmProvider.SendAsync(config, request, cancellationToken));

        var responses = await Task.WhenAll(tasks);
        var result = new LLMCompareResult
        {
            Responses = responses.ToList()
        };

        var successResponses = responses.Where(r => r.Success).ToList();
        result.Success = successResponses.Count > 0;

        if (successResponses.Count > 0)
        {
            result.BestResponse = SelectBestResponse(successResponses);
            result.VotedContent = result.BestResponse.Content;
            result.ConsensusRate = CalculateConsensusRate(successResponses);
        }

        return result;
    }

    public async Task<List<LLMConfig>> GetAvailableModelsAsync()
    {
        var configs = await GetModelConfigsAsync();
        return configs.Values.Where(m => m.IsEnabled).OrderBy(m => m.Priority).ToList();
    }

    public async Task<LLMConfig?> GetModelConfigAsync(string modelId)
    {
        var configs = await GetModelConfigsAsync();
        return configs.TryGetValue(modelId, out var config) ? config : null;
    }

    public async Task RefreshModelConfigsAsync()
    {
        _cache.Remove(CacheKey);
        _cache.Remove(CacheKeyAll);
        await GetModelConfigsAsync();
    }

    public async Task<List<LLMConfig>> GetAllModelsAsync()
    {
        var configs = await GetAllModelConfigsAsync();
        return configs.Values.OrderBy(m => m.Priority).ToList();
    }

    public async Task<LLMConfig> AddModelConfigAsync(LLMConfig config)
    {
        var entity = MapToEntity(config);
        entity.CreatedAt = DateTime.Now;
        entity.UpdatedAt = DateTime.Now;
        _dbContext.LLMModelConfig.Add(entity);
        await _dbContext.SaveChangesAsync();

        _cache.Remove(CacheKey);
        _cache.Remove(CacheKeyAll);
        _logger.LogInformation("Added LLM model config: {ModelId}", config.Id);
        return MapToConfig(entity);
    }

    public async Task<LLMConfig?> UpdateModelConfigAsync(LLMConfig config)
    {
        var entity = await _dbContext.LLMModelConfig.FindAsync(config.Id);
        if (entity == null) return null;

        entity.Name = config.Name;
        entity.BaseUrl = config.BaseUrl;
        entity.ApiKey = config.ApiKey;
        entity.Model = config.Model;
        entity.IsEnabled = config.IsEnabled;
        entity.Priority = config.Priority;
        entity.TimeoutSeconds = config.TimeoutSeconds;
        entity.MaxTokens = config.MaxTokens;
        entity.Temperature = config.Temperature;
        entity.Description = config.Description;
        entity.EnableThinking = config.EnableThinking;
        entity.ThinkingFormat = config.ThinkingFormat;
        entity.ThinkingBudgetTokens = config.ThinkingBudgetTokens;
        entity.ReasoningEffort = config.ReasoningEffort;
        entity.SupportsMultimodal = config.SupportsMultimodal;
        entity.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync();

        _cache.Remove(CacheKey);
        _cache.Remove(CacheKeyAll);
        _logger.LogInformation("Updated LLM model config: {ModelId}", config.Id);
        return MapToConfig(entity);
    }

    public async Task<bool> DeleteModelConfigAsync(string modelId)
    {
        var entity = await _dbContext.LLMModelConfig.FindAsync(modelId);
        if (entity == null) return false;

        _dbContext.LLMModelConfig.Remove(entity);
        await _dbContext.SaveChangesAsync();

        _cache.Remove(CacheKey);
        _cache.Remove(CacheKeyAll);
        _logger.LogInformation("Deleted LLM model config: {ModelId}", modelId);
        return true;
    }

    public async Task<DeepSeekBalance> GetDeepSeekBalanceAsync(string modelId, CancellationToken cancellationToken = default)
    {
        // 含禁用模型一并取，禁用的 DeepSeek 也能查余额
        var configs = await GetAllModelConfigsAsync();
        if (!configs.TryGetValue(modelId, out var config))
        {
            return new DeepSeekBalance { Success = false, ErrorMessage = $"Model not found: {modelId}" };
        }

        if (!config.BaseUrl.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
        {
            return new DeepSeekBalance { Success = false, ErrorMessage = "仅 api.deepseek.com 模型支持余额查询" };
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

            // DeepSeek 余额端点固定为 /user/balance（兼容 BaseUrl 带或不带 /v1）
            var host = config.BaseUrl.TrimEnd('/');
            if (host.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                host = host[..^3];
            var url = $"{host.TrimEnd('/')}/user/balance";

            var httpResponse = await client.GetAsync(url, cancellationToken);
            var json = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("DeepSeek balance query failed: {Status} - {Body}", httpResponse.StatusCode, json);
                return new DeepSeekBalance { Success = false, ErrorMessage = $"{(int)httpResponse.StatusCode} {httpResponse.StatusCode}" };
            }

            var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            var result = new DeepSeekBalance
            {
                Success = true,
                IsAvailable = root.TryGetProperty("is_available", out var avail) && avail.GetBoolean()
            };

            if (root.TryGetProperty("balance_infos", out var infos) && infos.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var info in infos.EnumerateArray())
                {
                    result.BalanceInfos.Add(new DeepSeekBalanceInfo
                    {
                        Currency = info.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "",
                        TotalBalance = info.TryGetProperty("total_balance", out var t) ? t.GetString() ?? "" : "",
                        GrantedBalance = info.TryGetProperty("granted_balance", out var g) ? g.GetString() ?? "" : "",
                        ToppedUpBalance = info.TryGetProperty("topped_up_balance", out var u) ? u.GetString() ?? "" : ""
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeepSeek balance query failed for model {ModelId}", modelId);
            return new DeepSeekBalance { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<LLMResponse> TestModelAsync(string modelId, string userPrompt, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        // 含禁用模型一并取，启用前也能测连通性
        var configs = await GetAllModelConfigsAsync();
        if (!configs.TryGetValue(modelId, out var config))
        {
            return new LLMResponse { Success = false, ErrorMessage = $"Model not found: {modelId}" };
        }

        return await TestConfigAsync(config, userPrompt, systemPrompt, cancellationToken);
    }

    public async Task<LLMResponse> TestConfigAsync(LLMConfig config, string userPrompt, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Model) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return new LLMResponse { Success = false, ErrorMessage = "API 地址、模型标识、API Key 不能为空" };
        }

        var request = new LLMRequest
        {
            UserPrompt = string.IsNullOrWhiteSpace(userPrompt) ? "你好，请用一句话简单自我介绍。" : userPrompt,
            SystemPrompt = systemPrompt,
            ModelId = config.Id
        };

        // 直连 Provider，绕过 IsEnabled 校验，不读库
        return await _llmProvider.SendAsync(config, request, cancellationToken);
    }

    private async Task<Dictionary<string, LLMConfig>> GetModelConfigsAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, LLMConfig>? cachedConfigs) && cachedConfigs != null)
        {
            return cachedConfigs;
        }

        try
        {
            var entities = await _dbContext.LLMModelConfig
                .Where(e => e.IsEnabled)
                .OrderBy(e => e.Priority)
                .ToListAsync();

            var configs = new Dictionary<string, LLMConfig>();
            foreach (var entity in entities)
            {
                var config = MapToConfig(entity);
                configs[config.Id] = config;
            }

            _cache.Set(CacheKey, configs, CacheExpiry);
            _logger.LogInformation("Loaded {Count} LLM model configs", configs.Count);

            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load LLM model configs");
            return new Dictionary<string, LLMConfig>();
        }
    }

    private static string? GetDefaultModelId(Dictionary<string, LLMConfig> configs)
    {
        return configs.Values
            .Where(m => m.IsEnabled)
            .OrderBy(m => m.Priority)
            .FirstOrDefault()?.Id;
    }

    private static LLMResponse SelectBestResponse(List<LLMResponse> responses)
    {
        if (responses.Count == 1) return responses[0];

        var grouped = responses
            .GroupBy(r => NormalizeContent(r.Content))
            .OrderByDescending(g => g.Count())
            .First();

        return grouped.First();
    }

    private static double CalculateConsensusRate(List<LLMResponse> responses)
    {
        if (responses.Count <= 1) return 1.0;

        var grouped = responses
            .GroupBy(r => NormalizeContent(r.Content))
            .OrderByDescending(g => g.Count())
            .First();

        return (double)grouped.Count() / responses.Count;
    }

    private static string NormalizeContent(string content)
    {
        return content.Trim().Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static LLMConfig MapToConfig(LLMModelConfigEntity entity)
    {
        return new LLMConfig
        {
            Id = entity.Id,
            Name = entity.Name,
            BaseUrl = entity.BaseUrl,
            ApiKey = entity.ApiKey,
            Model = entity.Model,
            IsEnabled = entity.IsEnabled,
            Priority = entity.Priority,
            TimeoutSeconds = entity.TimeoutSeconds,
            MaxTokens = entity.MaxTokens,
            Temperature = entity.Temperature,
            Description = entity.Description,
            EnableThinking = entity.EnableThinking,
            ThinkingFormat = entity.ThinkingFormat,
            ThinkingBudgetTokens = entity.ThinkingBudgetTokens,
            ReasoningEffort = entity.ReasoningEffort,
            SupportsMultimodal = entity.SupportsMultimodal
        };
    }

    private static LLMModelConfigEntity MapToEntity(LLMConfig config)
    {
        return new LLMModelConfigEntity
        {
            Id = config.Id,
            Name = config.Name,
            BaseUrl = config.BaseUrl,
            ApiKey = config.ApiKey,
            Model = config.Model,
            IsEnabled = config.IsEnabled,
            Priority = config.Priority,
            TimeoutSeconds = config.TimeoutSeconds,
            MaxTokens = config.MaxTokens,
            Temperature = config.Temperature,
            Description = config.Description,
            EnableThinking = config.EnableThinking,
            ThinkingFormat = config.ThinkingFormat,
            ThinkingBudgetTokens = config.ThinkingBudgetTokens,
            ReasoningEffort = config.ReasoningEffort,
            SupportsMultimodal = config.SupportsMultimodal
        };
    }

    private async Task<Dictionary<string, LLMConfig>> GetAllModelConfigsAsync()
    {
        if (_cache.TryGetValue(CacheKeyAll, out Dictionary<string, LLMConfig>? cachedConfigs) && cachedConfigs != null)
        {
            return cachedConfigs;
        }

        try
        {
            var entities = await _dbContext.LLMModelConfig
                .OrderBy(e => e.Priority)
                .ToListAsync();

            var configs = new Dictionary<string, LLMConfig>();
            foreach (var entity in entities)
            {
                var config = MapToConfig(entity);
                configs[config.Id] = config;
            }

            _cache.Set(CacheKeyAll, configs, CacheExpiry);
            _logger.LogInformation("Loaded {Count} LLM model configs (all)", configs.Count);

            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load all LLM model configs");
            return new Dictionary<string, LLMConfig>();
        }
    }
}
