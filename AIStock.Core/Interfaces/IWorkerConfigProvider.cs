namespace AIStock.Core.Interfaces;

/// <summary>
/// Worker 任务配置读写中心。配置按"段(section)"存储 JSON（形状与 appsettings 对应段一致），
/// Worker 与 Web 共用同一张表：Web 端读写，Worker 端按短 TTL 缓存热读，改动无需重启即可生效。
/// </summary>
public interface IWorkerConfigProvider
{
    /// <summary>取某段配置并反序列化为 T（带 TTL 缓存）。无 DB 行时回退 T 的代码默认值。</summary>
    Task<T> GetAsync<T>(string section, CancellationToken ct = default) where T : class, new();

    /// <summary>取某段配置的原始 JSON（无 DB 行时返回 "{}"）。</summary>
    Task<string> GetRawAsync(string section, CancellationToken ct = default);

    /// <summary>写入/覆盖某段配置 JSON（同时刷新本进程缓存，立即生效）。</summary>
    Task SaveAsync(string section, string configJson, CancellationToken ct = default);

    /// <summary>
    /// 种子化：若该段尚无 DB 行，则用 defaultValue 序列化后写入（已存在则不动）。
    /// 供 Worker 启动时把 appsettings 默认值固化进 DB，之后 DB 即权威源。
    /// </summary>
    Task EnsureSeededAsync(string section, object defaultValue, CancellationToken ct = default);
}
