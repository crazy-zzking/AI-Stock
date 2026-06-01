using System.Text.Json;
using System.Text.RegularExpressions;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Prompt.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Prompt.Services;

/// <summary>
/// Prompt注册中心 — 从数据库加载Prompt模板，支持版本管理和缓存
/// </summary>
public class PromptRegistryService : IPromptRegistry
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILogger<PromptRegistryService> _logger;
    private readonly Dictionary<string, List<PromptTemplate>> _promptsByCategory = new();
    private readonly Dictionary<string, PromptTemplate> _promptsByName = new();  // key: "name:version"
    private bool _loaded = false;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public PromptRegistryService(
        AIStockDbContext dbContext,
        ILogger<PromptRegistryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PromptTemplate?> GetPromptAsync(string name, string version = "latest")
    {
        await EnsureLoadedAsync();

        if (_promptsByName.TryGetValue($"{name}:{version}", out var template))
            return template;

        if (version == "latest")
        {
            return _promptsByName
                .Where(kv => kv.Key.StartsWith($"{name}:v"))
                .OrderByDescending(kv => kv.Key)
                .Select(kv => kv.Value)
                .FirstOrDefault();
        }

        return null;
    }

    public async Task<LLMRequest> BuildRequestAsync(string promptName, Dictionary<string, string> variables, string? version = null)
    {
        var template = await GetPromptAsync(promptName, version ?? "latest");
        if (template == null)
            throw new InvalidOperationException($"Prompt '{promptName}' not found");

        var systemPrompt = template.SystemPrompt;
        var userPrompt = template.UserPrompt;

        foreach (var (key, value) in variables)
        {
            var placeholder = $"{{{key}}}";
            systemPrompt = systemPrompt?.Replace(placeholder, value);
            userPrompt = userPrompt.Replace(placeholder, value);
        }

        return new LLMRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            ModelId = template.Model,
            Temperature = (decimal)template.Temperature,
            MaxTokens = template.MaxTokens
        };
    }

    public async Task<List<PromptInfo>> ListPromptsAsync(string? category = null)
    {
        await EnsureLoadedAsync();

        var templates = string.IsNullOrEmpty(category)
            ? _promptsByName.Values.ToList()
            : _promptsByCategory.GetValueOrDefault(category, new List<PromptTemplate>());

        return templates.Select(t => new PromptInfo
        {
            Name = t.Name,
            Version = t.Version,
            Category = t.Category,
            Description = t.Description
        }).ToList();
    }

    public async Task ReloadAsync()
    {
        _promptsByCategory.Clear();
        _promptsByName.Clear();
        _loaded = false;
        await EnsureLoadedAsync();
        _logger.LogInformation("Prompts reloaded: {Count} templates loaded", _promptsByName.Count);
    }

    public async Task SavePromptAsync(PromptTemplate template)
    {
        var entity = await _dbContext.PromptTemplate
            .FirstOrDefaultAsync(e => e.Name == template.Name && e.Version == template.Version);

        if (entity == null)
        {
            entity = new PromptTemplateEntity
            {
                Name = template.Name,
                Version = template.Version,
                CreatedAt = DateTime.Now
            };
            _dbContext.PromptTemplate.Add(entity);
            _logger.LogInformation("Created prompt: {Name}:{Version}", template.Name, template.Version);
        }
        else
        {
            _logger.LogInformation("Updated prompt: {Name}:{Version}", template.Name, template.Version);
        }

        entity.Category = template.Category;
        entity.Description = template.Description;
        entity.Model = template.Model;
        entity.Temperature = template.Temperature;
        entity.MaxTokens = template.MaxTokens;
        entity.Variables = SerializeVariables(template.Variables);
        entity.SystemPrompt = template.SystemPrompt;
        entity.UserPrompt = template.UserPrompt;
        entity.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync();

        // Reload cache
        await ReloadAsync();
    }

    public async Task<bool> DeletePromptAsync(string name, string version)
    {
        var entity = await _dbContext.PromptTemplate
            .FirstOrDefaultAsync(e => e.Name == name && e.Version == version);

        if (entity == null) return false;

        _dbContext.PromptTemplate.Remove(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted prompt: {Name}:{Version}", name, version);

        await ReloadAsync();
        return true;
    }

    /// <summary>
    /// 初始化默认Prompt到数据库（仅当表为空时执行，硬编码数据确保UTF-8编码正确）
    /// </summary>
    public async Task SeedDefaultPromptsAsync()
    {
        var hasAny = await _dbContext.PromptTemplate.AnyAsync();
        if (hasAny)
        {
            _logger.LogInformation("Prompt table already has data, skipping seed");
            return;
        }

        var defaults = new PromptTemplate[]
        {
            new()
            {
                Name = "risk-assessment",
                Version = "v1",
                Category = "risk",
                Description = "风险评估Prompt — 综合评估交易风险等级",
                Model = "deepseek-v3",
                Temperature = 0.1,
                MaxTokens = 1000,
                Variables = new List<string> { "code", "signal_type", "position_percent", "market_sentiment", "volatility" },
                SystemPrompt = "你是一位风险控制专家。请基于提供的数据评估交易风险。\n风险等级：低(Low)、中(Medium)、高(High)、严重(Critical)。\n风控优先级永远高于收益追求。",
                UserPrompt = "请评估以下交易的风险：\n\n股票：{code}\n信号类型：{signal_type}\n仓位占比：{position_percent}%\n市场情绪：{market_sentiment}\n波动率：{volatility}\n\n请输出风险等级及具体建议。"
            },
            new()
            {
                Name = "signal-generation",
                Version = "v1",
                Category = "strategy",
                Description = "交易信号生成Prompt — 基于多维度数据生成买卖建议",
                Model = "deepseek-v3",
                Temperature = 0.2,
                MaxTokens = 1500,
                Variables = new List<string> { "code", "current_price", "trend", "rsi", "volume_change" },
                SystemPrompt = "你是一位量化交易策略分析师。请基于提供的数据生成交易信号。\n信号类型：买入(Buy)、卖出(Sell)、持有(Hold)。\n必须给出明确的置信度(0-100)和理由。",
                UserPrompt = "请为股票 {code} 生成交易信号：\n\n当前价格：{current_price}\n趋势：{trend}\nRSI：{rsi}\n成交量变化：{volume_change}\n\n请输出信号类型、置信度(0-100)和推荐仓位比例。"
            },
            new()
            {
                Name = "trend-analysis",
                Version = "v1",
                Category = "technical",
                Description = "股票趋势分析Prompt — 基于技术指标判断趋势方向",
                Model = "deepseek-v3",
                Temperature = 0.3,
                MaxTokens = 2000,
                Variables = new List<string> { "code", "current_price", "ma5", "ma10", "ma20", "ma60", "rsi", "macd", "volatility", "atr" },
                SystemPrompt = "你是一位专业的A股技术分析师。请基于提供的技术指标数据，对股票进行客观的趋势分析。\n分析需包含：趋势判断、支撑压力位、风险提示。\n请用中文回答，简洁专业。",
                UserPrompt = "请分析股票 {code} 的技术面：\n\n当前价格：{current_price}\n均线：MA5={ma5}, MA10={ma10}, MA20={ma20}, MA60={ma60}\nRSI(12)：{rsi}\nMACD：{macd}\n波动率：{volatility}\nATR：{atr}\n\n请给出趋势判断（上涨/下跌/震荡）及置信度。"
            }
        };

        foreach (var template in defaults)
        {
            await SavePromptAsync(template);
            _logger.LogInformation("Seeded default prompt: {Name}:{Version}", template.Name, template.Version);
        }

        _logger.LogInformation("Seed default prompts completed: {Count} templates", defaults.Length);
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _loadLock.WaitAsync();
        try
        {
            if (_loaded) return;
            await LoadPromptsAsync();
            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadPromptsAsync()
    {
        try
        {
            var entities = await _dbContext.PromptTemplate
                .AsNoTracking()
                .OrderBy(e => e.Name)
                .ThenByDescending(e => e.Version)
                .ToListAsync();

            foreach (var entity in entities)
            {
                var template = MapToTemplate(entity);

                var key = $"{template.Name}:{template.Version}";
                _promptsByName[key] = template;

                if (!_promptsByCategory.ContainsKey(template.Category))
                    _promptsByCategory[template.Category] = new List<PromptTemplate>();
                _promptsByCategory[template.Category].Add(template);
            }

            _logger.LogInformation("Loaded {Count} prompt templates from database", _promptsByName.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load prompt templates from database");
        }
    }

    private static PromptTemplate MapToTemplate(PromptTemplateEntity entity)
    {
        return new PromptTemplate
        {
            Name = entity.Name,
            Version = entity.Version,
            Category = entity.Category,
            Description = entity.Description,
            Model = entity.Model,
            Temperature = entity.Temperature,
            MaxTokens = entity.MaxTokens,
            Variables = DeserializeVariables(entity.Variables),
            SystemPrompt = entity.SystemPrompt,
            UserPrompt = entity.UserPrompt
        };
    }

    private static string? SerializeVariables(List<string> variables)
    {
        if (variables == null || variables.Count == 0) return null;
        return JsonSerializer.Serialize(variables, JsonOptions);
    }

    private static List<string> DeserializeVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}

/// <summary>
/// Prompt注册中心配置
/// </summary>
public class PromptRegistryOptions
{
    /// <summary>
    /// Prompt YAML文件根目录
    /// </summary>
    public string RootPath { get; set; } = "prompts";
}
