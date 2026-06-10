using System.Text.Json;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection;

/// <summary>
/// 选股配置中心 — 版本化存取 SelectionCriteria（含权重）。
/// 选股低频且配置表极小（走索引），故每次查库、不做内存缓存，避免缓存失效复杂度。
/// </summary>
public class SelectionConfigService
{
    /// <summary>默认配置名（当前单策略；多策略阶段每策略一个名）</summary>
    public const string DefaultName = "默认";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false, Encoder = AIStock.Core.Json.AppJson.CjkEncoder };

    private readonly AIStockDbContext _db;
    private readonly ILogger<SelectionConfigService> _logger;

    public SelectionConfigService(AIStockDbContext db, ILogger<SelectionConfigService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>代码内置默认条件（未配置任何版本时的回退；也供前端"恢复默认"）。</summary>
    public static SelectionCriteria GetDefaultCriteria() => new();

    /// <summary>
    /// 取当前生效的选股条件：读 name 下 IsActive 的版本并反序列化；
    /// 无生效版本或反序列化失败时回退代码默认。
    /// </summary>
    public async Task<SelectionCriteria> GetActiveCriteriaAsync(string name = DefaultName, CancellationToken ct = default)
    {
        var active = await _db.SelectionConfig
            .Where(c => c.Name == name && c.IsActive)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (active == null) return GetDefaultCriteria();

        var criteria = Deserialize(active.ConfigJson);
        if (criteria == null)
        {
            _logger.LogWarning("选股配置 {Name}/{Version} 反序列化失败，回退代码默认", name, active.Version);
            return GetDefaultCriteria();
        }
        return criteria;
    }

    /// <summary>当前生效版本的元信息（含 JSON）。无则 null。</summary>
    public async Task<SelectionConfigEntity?> GetActiveAsync(string name = DefaultName, CancellationToken ct = default)
        => await _db.SelectionConfig
            .Where(c => c.Name == name && c.IsActive)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>列出所有配置版本（按名、创建时间倒序），ConfigJson 一并返回供前端查看/对比。</summary>
    public async Task<List<SelectionConfigEntity>> ListAsync(CancellationToken ct = default)
        => await _db.SelectionConfig
            .OrderBy(c => c.Name)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<SelectionConfigEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.SelectionConfig.FirstOrDefaultAsync(c => c.Id == id, ct);

    /// <summary>
    /// 保存为新版本。同 Name+Version 已存在则覆盖其 JSON/备注（视为修订草稿）。
    /// activate=true 时把同 Name 其它版本置为非生效、本条生效。
    /// </summary>
    public async Task<SelectionConfigEntity> SaveAsync(
        string name, string version, SelectionCriteria criteria,
        string? remark, bool activate, CancellationToken ct = default)
    {
        name = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        version = string.IsNullOrWhiteSpace(version) ? "v1.0" : version.Trim();
        var json = JsonSerializer.Serialize(criteria, JsonOpts);

        var existing = await _db.SelectionConfig
            .FirstOrDefaultAsync(c => c.Name == name && c.Version == version, ct);
        if (existing != null)
        {
            existing.ConfigJson = json;
            existing.Remark = remark;
        }
        else
        {
            existing = new SelectionConfigEntity
            {
                Name = name,
                Version = version,
                ConfigJson = json,
                Remark = remark,
                IsActive = false,
            };
            _db.SelectionConfig.Add(existing);
        }

        await _db.SaveChangesAsync(ct);

        if (activate)
            await ActivateAsync(existing.Id, ct);

        _logger.LogInformation("选股配置已保存：{Name}/{Version}（{Activate}）",
            name, version, activate ? "并激活" : "未激活");
        return existing;
    }

    /// <summary>激活指定版本：同 Name 其它版本全部置为非生效，本条置生效。</summary>
    public async Task<bool> ActivateAsync(long id, CancellationToken ct = default)
    {
        var target = await _db.SelectionConfig.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (target == null) return false;

        var siblings = await _db.SelectionConfig
            .Where(c => c.Name == target.Name && c.IsActive && c.Id != id)
            .ToListAsync(ct);
        foreach (var s in siblings) s.IsActive = false;
        target.IsActive = true;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("选股配置已激活：{Name}/{Version}", target.Name, target.Version);
        return true;
    }

    /// <summary>删除某版本（生效中的不允许删，先激活其它版本）。</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var target = await _db.SelectionConfig.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (target == null || target.IsActive) return false;
        _db.SelectionConfig.Remove(target);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static SelectionCriteria? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SelectionCriteria>(json); }
        catch { return null; }
    }
}
