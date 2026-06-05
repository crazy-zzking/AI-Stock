using System.Text.Json;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection.Strategies;

/// <summary>
/// 默认策略提供者：内置策略(DI 注册) + 数据库自建策略(strategy_definition → ConfigurableSelectionStrategy)。
/// 同 key 以内置优先（用户不能覆盖/顶替内置策略）。每次查询实时读库，配置表极小、走唯一索引。
/// </summary>
public class SelectionStrategyProvider : ISelectionStrategyProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IEnumerable<ISelectionStrategy> _builtins;
    private readonly AIStockDbContext _db;
    private readonly ILogger<SelectionStrategyProvider> _logger;

    public SelectionStrategyProvider(
        IEnumerable<ISelectionStrategy> builtins, AIStockDbContext db, ILogger<SelectionStrategyProvider> logger)
    {
        _builtins = builtins;
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ISelectionStrategy>> GetAllAsync(CancellationToken ct = default)
    {
        // 内置在前（lowdip 优先），保持原有展示顺序
        var ordered = _builtins
            .OrderBy(s => s.Key == StrategyKeys.LowDip ? 0 : 1).ThenBy(s => s.Key)
            .ToList();
        var keys = new HashSet<string>(ordered.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

        var defs = await _db.StrategyDefinition
            .Where(d => d.Enabled)
            .OrderBy(d => d.Key)
            .ToListAsync(ct);

        foreach (var d in defs)
        {
            if (keys.Contains(d.Key)) // 不允许顶替内置 key
            {
                _logger.LogWarning("自建策略 key [{Key}] 与内置冲突，已忽略", d.Key);
                continue;
            }
            var strategy = TryBuild(d.DefinitionJson, d.Key);
            if (strategy != null) ordered.Add(strategy);
        }

        return ordered;
    }

    public async Task<ISelectionStrategy> ResolveAsync(string? key, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(key))
        {
            var hit = all.FirstOrDefault(s => string.Equals(s.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return all.First(s => s.Key == StrategyKeys.LowDip);
    }

    private ConfigurableSelectionStrategy? TryBuild(string json, string key)
    {
        try
        {
            var def = JsonSerializer.Deserialize<StrategyDefinition>(json, JsonOpts);
            if (def == null) { _logger.LogWarning("策略 [{Key}] 定义反序列化为空，跳过", key); return null; }
            def.Key = key; // 以表中 key 为准
            return new ConfigurableSelectionStrategy(def);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "策略 [{Key}] 定义解析失败，跳过", key);
            return null;
        }
    }
}
