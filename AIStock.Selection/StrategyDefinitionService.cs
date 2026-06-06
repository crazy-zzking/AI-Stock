using System.Text.Json;
using System.Text.RegularExpressions;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection;

/// <summary>
/// 自建选股策略定义的 CRUD —— 维护 strategy_definition 表（"口径定义"，权重/阈值另走 selection_config）。
/// 负责 key 唯一性 / 格式 / 内置保护 / 因子口径白名单等校验；写库后 SelectionStrategyProvider 实时可见。
/// 校验失败抛 <see cref="StrategyDefValidationException"/>，由控制器转 400。
/// </summary>
public class StrategyDefinitionService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,49}$", RegexOptions.Compiled);

    /// <summary>内置 key（不可被自建策略占用/顶替）。</summary>
    private static readonly HashSet<string> BuiltinKeys =
        new(new[] { StrategyKeys.LowDip, StrategyKeys.Trend, StrategyKeys.Theme }, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FactorKindWhitelist =
        new(new[] { "lowdip", "trend" }, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PenaltyWhitelist =
        new(new[] { "none", "limitup" }, StringComparer.OrdinalIgnoreCase);

    private readonly AIStockDbContext _db;
    private readonly ILogger<StrategyDefinitionService> _logger;

    public StrategyDefinitionService(AIStockDbContext db, ILogger<StrategyDefinitionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>三个内置策略的定义模板（前端"以内置为模板克隆"用）。</summary>
    public static IReadOnlyList<StrategyDefinition> Builtins() => new[]
    {
        BuiltinStrategyDefinitions.LowDip(),
        BuiltinStrategyDefinitions.Trend(),
        BuiltinStrategyDefinitions.Theme(),
    };

    /// <summary>列出全部自建策略（按 key 排序）。</summary>
    public async Task<List<StrategyDefinitionEntity>> ListAsync(CancellationToken ct = default)
        => await _db.StrategyDefinition.OrderBy(d => d.Key).ToListAsync(ct);

    /// <summary>把实体的 DefinitionJson 反序列化成结构化定义（解析失败回退一个仅含元信息的壳）。</summary>
    public StrategyDefinition ParseDefinition(StrategyDefinitionEntity e)
    {
        try
        {
            var def = JsonSerializer.Deserialize<StrategyDefinition>(e.DefinitionJson, JsonOpts);
            if (def != null) { def.Key = e.Key; return def; }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "策略 [{Key}] 定义解析失败，返回壳", e.Key);
        }
        return new StrategyDefinition
        {
            Key = e.Key, Name = e.Name, Description = e.Description, PreferredRegime = e.PreferredRegime,
        };
    }

    /// <summary>新建自建策略。key 取自 def.Key。</summary>
    public async Task<StrategyDefinitionEntity> CreateAsync(StrategyDefinition def, bool enabled, CancellationToken ct = default)
    {
        Normalize(def);
        Validate(def);

        if (await _db.StrategyDefinition.AnyAsync(d => d.Key == def.Key, ct))
            throw new StrategyDefValidationException($"策略 key「{def.Key}」已存在");

        var entity = new StrategyDefinitionEntity
        {
            Key = def.Key,
            Name = def.Name,
            Description = def.Description,
            PreferredRegime = def.PreferredRegime,
            Enabled = enabled,
            DefinitionJson = JsonSerializer.Serialize(def, JsonOpts),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        _db.StrategyDefinition.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("自建策略已创建：{Key}/{Name}（enabled={Enabled}）", def.Key, def.Name, enabled);
        return entity;
    }

    /// <summary>更新自建策略。key 可改，但仍受唯一性/格式/内置保护约束。</summary>
    public async Task<StrategyDefinitionEntity?> UpdateAsync(long id, StrategyDefinition def, bool enabled, CancellationToken ct = default)
    {
        var entity = await _db.StrategyDefinition.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity == null) return null;

        Normalize(def);
        Validate(def);

        if (await _db.StrategyDefinition.AnyAsync(d => d.Key == def.Key && d.Id != id, ct))
            throw new StrategyDefValidationException($"策略 key「{def.Key}」已被其它策略占用");

        entity.Key = def.Key;
        entity.Name = def.Name;
        entity.Description = def.Description;
        entity.PreferredRegime = def.PreferredRegime;
        entity.Enabled = enabled;
        entity.DefinitionJson = JsonSerializer.Serialize(def, JsonOpts);
        entity.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("自建策略已更新：{Key}/{Name}（enabled={Enabled}）", def.Key, def.Name, enabled);
        return entity;
    }

    /// <summary>删除自建策略。</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _db.StrategyDefinition.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity == null) return false;
        _db.StrategyDefinition.Remove(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("自建策略已删除：{Key}", entity.Key);
        return true;
    }

    private static void Normalize(StrategyDefinition def)
    {
        def.Key = (def.Key ?? string.Empty).Trim().ToLowerInvariant();
        def.Name = (def.Name ?? string.Empty).Trim();
        def.Description = (def.Description ?? string.Empty).Trim();
        def.PreferredRegime = (def.PreferredRegime ?? string.Empty).Trim();
        def.Penalty = (def.Penalty ?? "limitup").Trim().ToLowerInvariant();
        def.FactorKinds ??= new StrategyFactorKinds();
        def.FactorKinds.Technical = (def.FactorKinds.Technical ?? "lowdip").Trim().ToLowerInvariant();
        def.FactorKinds.Position = (def.FactorKinds.Position ?? "lowdip").Trim().ToLowerInvariant();
        def.Filters ??= new StrategyFilters();
        if (string.IsNullOrWhiteSpace(def.CoreLogicTemplate)) def.CoreLogicTemplate = null;
    }

    private static void Validate(StrategyDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Key) || !KeyPattern.IsMatch(def.Key))
            throw new StrategyDefValidationException("策略 key 必须以小写字母开头，仅含小写字母/数字/下划线，长度 2-50");
        if (BuiltinKeys.Contains(def.Key))
            throw new StrategyDefValidationException($"「{def.Key}」是内置策略 key，不可创建/占用");
        if (string.IsNullOrWhiteSpace(def.Name))
            throw new StrategyDefValidationException("策略展示名不能为空");
        if (!PenaltyWhitelist.Contains(def.Penalty))
            throw new StrategyDefValidationException("惩罚口径只能是 none 或 limitup");
        if (!FactorKindWhitelist.Contains(def.FactorKinds.Technical))
            throw new StrategyDefValidationException("技术因子口径只能是 lowdip 或 trend");
        if (!FactorKindWhitelist.Contains(def.FactorKinds.Position))
            throw new StrategyDefValidationException("位置因子口径只能是 lowdip 或 trend");
    }
}

/// <summary>自建策略校验失败（控制器据此返回 400 + 中文消息）。</summary>
public class StrategyDefValidationException : Exception
{
    public StrategyDefValidationException(string message) : base(message) { }
}
