using System.Collections.Concurrent;
using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Infrastructure.Configuration;

/// <summary>
/// Worker 任务配置中心实现（<see cref="IWorkerConfigProvider"/>）。
/// 按段缓存原始 JSON（短 TTL），跨进程的改动经 TTL 自动拉取 → Worker 热生效。
/// 单例服务，经 IServiceScopeFactory 取 DbContext。
/// </summary>
public class WorkerConfigService : IWorkerConfigProvider
{
    /// <summary>缓存 TTL：Web 改完最迟 ~此时长后被 Worker 拉到。</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Encoder = AIStock.Core.Json.AppJson.CjkEncoder,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkerConfigService> _logger;
    private readonly ConcurrentDictionary<string, (DateTime ts, string json)> _cache = new();

    public WorkerConfigService(IServiceScopeFactory scopeFactory, ILogger<WorkerConfigService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<T> GetAsync<T>(string section, CancellationToken ct = default) where T : class, new()
    {
        var json = await GetRawAsync(section, ct);
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? new T();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker 配置段 {Section} 反序列化失败，回退默认", section);
            return new T();
        }
    }

    public async Task<string> GetRawAsync(string section, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(section, out var hit) && DateTime.UtcNow - hit.ts < CacheTtl)
            return hit.json;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var row = await db.WorkerConfig.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Section == section, ct);

        var json = string.IsNullOrWhiteSpace(row?.ConfigJson) ? "{}" : row!.ConfigJson;
        _cache[section] = (DateTime.UtcNow, json);
        return json;
    }

    public async Task SaveAsync(string section, string configJson, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var row = await db.WorkerConfig.FirstOrDefaultAsync(c => c.Section == section, ct);
        if (row == null)
        {
            row = new WorkerConfigEntity { Section = section, ConfigJson = configJson };
            db.WorkerConfig.Add(row);
        }
        else
        {
            row.ConfigJson = configJson;
        }
        await db.SaveChangesAsync(ct);

        _cache[section] = (DateTime.UtcNow, configJson); // 本进程立即生效
        _logger.LogInformation("Worker 配置段 {Section} 已保存", section);
    }

    public async Task EnsureSeededAsync(string section, object defaultValue, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var exists = await db.WorkerConfig.AnyAsync(c => c.Section == section, ct);
        if (exists) return;

        var json = JsonSerializer.Serialize(defaultValue, JsonOpts);
        db.WorkerConfig.Add(new WorkerConfigEntity { Section = section, ConfigJson = json });
        await db.SaveChangesAsync(ct);
        _cache[section] = (DateTime.UtcNow, json);
        _logger.LogInformation("Worker 配置段 {Section} 已种子化（appsettings → DB）", section);
    }
}
