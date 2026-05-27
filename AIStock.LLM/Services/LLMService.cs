using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AIStock.LLM.Services;

/// <summary>
/// LLM服务实现
/// </summary>
public class LLMService : ILLMService
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILLMProvider _llmProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LLMService> _logger;

    private const string CacheKey = "LLM_Model_Configs";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    public LLMService(
        AIStockDbContext dbContext,
        ILLMProvider llmProvider,
        IMemoryCache cache,
        ILogger<LLMService> logger)
    {
        _dbContext = dbContext;
        _llmProvider = llmProvider;
        _cache = cache;
        _logger = logger;
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
        await GetModelConfigsAsync();
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
            Description = entity.Description
        };
    }
}
