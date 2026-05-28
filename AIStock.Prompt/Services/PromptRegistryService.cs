using System.Text.RegularExpressions;
using AIStock.Core.Models;
using AIStock.Prompt.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AIStock.Prompt.Services;

/// <summary>
/// Prompt注册中心 — 从YAML文件加载Prompt模板，支持版本管理和热更新
/// </summary>
public class PromptRegistryService : IPromptRegistry
{
    private readonly PromptRegistryOptions _options;
    private readonly ILogger<PromptRegistryService> _logger;
    private readonly Dictionary<string, List<PromptTemplate>> _promptsByCategory = new();
    private readonly Dictionary<string, PromptTemplate> _promptsByName = new();  // key: "name:version"
    private readonly IDeserializer _yamlDeserializer;

    public PromptRegistryService(
        IOptions<PromptRegistryOptions> options,
        ILogger<PromptRegistryService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        LoadPrompts();
    }

    public PromptTemplate? GetPrompt(string name, string version = "latest")
    {
        if (_promptsByName.TryGetValue($"{name}:{version}", out var template))
            return template;

        // latest: 取最新版本
        if (version == "latest")
        {
            var allVersions = _promptsByName
                .Where(kv => kv.Key.StartsWith($"{name}:v"))
                .OrderByDescending(kv => kv.Key)
                .Select(kv => kv.Value);

            return allVersions.FirstOrDefault();
        }

        return null;
    }

    public LLMRequest BuildRequest(string promptName, Dictionary<string, string> variables, string? version = null)
    {
        var template = GetPrompt(promptName, version ?? "latest");
        if (template == null)
            throw new InvalidOperationException($"Prompt '{promptName}' not found");

        var systemPrompt = template.SystemPrompt;
        var userPrompt = template.UserPrompt;

        // 替换模板变量 {variableName}
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

    public List<PromptInfo> ListPrompts(string? category = null)
    {
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

    public Task ReloadAsync()
    {
        _promptsByCategory.Clear();
        _promptsByName.Clear();
        LoadPrompts();
        _logger.LogInformation("Prompts reloaded: {Count} templates loaded", _promptsByName.Count);
        return Task.CompletedTask;
    }

    private void LoadPrompts()
    {
        var rootPath = _options.RootPath;
        if (!Directory.Exists(rootPath))
        {
            _logger.LogWarning("Prompt root path not found: {Path}", rootPath);
            return;
        }

        foreach (var yamlFile in Directory.EnumerateFiles(rootPath, "*.yaml", SearchOption.AllDirectories))
        {
            try
            {
                var category = Path.GetFileName(Path.GetDirectoryName(yamlFile)) ?? "default";
                var yamlContent = File.ReadAllText(yamlFile);
                var template = _yamlDeserializer.Deserialize<PromptTemplate>(yamlContent);

                if (template == null || string.IsNullOrEmpty(template.Name))
                {
                    _logger.LogWarning("Skipping invalid prompt file: {File}", yamlFile);
                    continue;
                }

                template.Category = category;

                var key = $"{template.Name}:{template.Version}";
                _promptsByName[key] = template;

                if (!_promptsByCategory.ContainsKey(category))
                    _promptsByCategory[category] = new List<PromptTemplate>();
                _promptsByCategory[category].Add(template);

                _logger.LogDebug("Loaded prompt: {Name}:{Version} from {File}",
                    template.Name, template.Version, yamlFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load prompt from {File}", yamlFile);
            }
        }

        _logger.LogInformation("Loaded {Count} prompt templates from {Path}",
            _promptsByName.Count, rootPath);
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
