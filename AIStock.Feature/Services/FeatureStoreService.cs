using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AIStock.Feature.Services;

/// <summary>
/// Feature Store实现（使用Sorted Set避免KEYS命令）
/// </summary>
public class FeatureStoreService : IFeatureStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<FeatureStoreService> _logger;

    private const string KeyPrefix = "feature:";
    private const string HistoryKeyPrefix = "feature:history:";
    private const string IndexKeyPrefix = "feature:index:";

    public FeatureStoreService(IConnectionMultiplexer redis, ILogger<FeatureStoreService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task SaveFeaturesAsync(string code, DateTime dateTime, TechnicalIndicator indicators)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}{code}";
            var indexKey = $"{IndexKeyPrefix}{code}";
            var timestamp = dateTime.ToUniversalTime().Ticks;

            var json = JsonSerializer.Serialize(indicators);

            // 保存最新特征（热数据，24h TTL）
            await db.StringSetAsync(key, json, TimeSpan.FromHours(24));

            // 保存到Sorted Set索引（按时间戳排序，30d TTL）
            await db.SortedSetAddAsync(indexKey, json, timestamp);
            await db.KeyExpireAsync(indexKey, TimeSpan.FromDays(30));

            _logger.LogDebug("Saved features for {Code} at {DateTime}", code, dateTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save features for {Code}", code);
            throw;
        }
    }

    public async Task<TechnicalIndicator?> GetLatestFeaturesAsync(string code)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{KeyPrefix}{code}";

            var json = await db.StringGetAsync(key);
            if (json.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<TechnicalIndicator>(json!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get features for {Code}", code);
            return null;
        }
    }

    public async Task<List<TechnicalIndicator>> GetHistoricalFeaturesAsync(string code, DateTime startTime, DateTime endTime)
    {
        try
        {
            var db = _redis.GetDatabase();
            var indexKey = $"{IndexKeyPrefix}{code}";
            var startTicks = startTime.ToUniversalTime().Ticks;
            var endTicks = endTime.ToUniversalTime().Ticks;

            // 使用Sorted Set的范围查询，替代KEYS命令
            var entries = await db.SortedSetRangeByScoreAsync(indexKey, startTicks, endTicks);

            var results = new List<TechnicalIndicator>();
            foreach (var entry in entries)
            {
                if (entry.IsNullOrEmpty) continue;
                var indicator = JsonSerializer.Deserialize<TechnicalIndicator>(entry!);
                if (indicator != null)
                {
                    results.Add(indicator);
                }
            }

            return results.OrderBy(i => i.DateTime).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get historical features for {Code}", code);
            return new List<TechnicalIndicator>();
        }
    }

    public async Task BatchSaveFeaturesAsync(List<(string Code, DateTime DateTime, TechnicalIndicator Indicators)> features)
    {
        try
        {
            var db = _redis.GetDatabase();
            var tasks = new List<Task>();

            foreach (var (code, dateTime, indicators) in features)
            {
                var key = $"{KeyPrefix}{code}";
                var indexKey = $"{IndexKeyPrefix}{code}";
                var timestamp = dateTime.ToUniversalTime().Ticks;
                var json = JsonSerializer.Serialize(indicators);

                tasks.Add(db.StringSetAsync(key, json, TimeSpan.FromHours(24)));
                tasks.Add(db.SortedSetAddAsync(indexKey, json, timestamp));
                tasks.Add(db.KeyExpireAsync(indexKey, TimeSpan.FromDays(30)));
            }

            await Task.WhenAll(tasks);
            _logger.LogInformation("Batch saved features for {Count} stocks", features.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch save features");
            throw;
        }
    }

    public async Task CleanupExpiredFeaturesAsync(TimeSpan maxAge)
    {
        try
        {
            var db = _redis.GetDatabase();
            var cutoffTicks = DateTime.UtcNow.Subtract(maxAge).Ticks;

            // 通过Sorted Set的索引前缀查找所有索引key
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var deletedCount = 0;

            // 使用SCAN替代KEYS（更安全）
            foreach (var key in server.Keys(pattern: $"{IndexKeyPrefix}*"))
            {
                // 使用ZREMRANGEBYSCORE按时间范围删除过期数据
                var removed = await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoffTicks);
                deletedCount += (int)removed;

                // 如果Sorted Set为空，删除整个key
                var length = await db.SortedSetLengthAsync(key);
                if (length == 0)
                {
                    await db.KeyDeleteAsync(key);
                }
            }

            _logger.LogInformation("Cleaned up {Count} expired features", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired features");
            throw;
        }
    }
}
