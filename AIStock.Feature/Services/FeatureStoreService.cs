using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AIStock.Feature.Services;

/// <summary>
/// Feature Store实现
/// </summary>
public class FeatureStoreService : IFeatureStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<FeatureStoreService> _logger;

    private const string KeyPrefix = "feature:";
    private const string HistoryKeyPrefix = "feature:history:";

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
            var historyKey = $"{HistoryKeyPrefix}{code}:{dateTime:yyyyMMddHHmmss}";

            var json = JsonSerializer.Serialize(indicators);

            await db.StringSetAsync(key, json, TimeSpan.FromHours(24));
            await db.StringSetAsync(historyKey, json, TimeSpan.FromDays(30));

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
            var pattern = $"{HistoryKeyPrefix}{code}:*";
            var keys = new List<RedisKey>();

            var server = _redis.GetServer(_redis.GetEndPoints().First());
            foreach (var key in server.Keys(pattern: pattern))
            {
                var keyStr = key.ToString();
                var dateStr = keyStr.Split(':').Last();
                if (DateTime.TryParseExact(dateStr, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    if (date >= startTime && date <= endTime)
                    {
                        keys.Add(key);
                    }
                }
            }

            var results = new List<TechnicalIndicator>();
            foreach (var key in keys)
            {
                var json = await db.StringGetAsync(key);
                if (!json.IsNullOrEmpty)
                {
                    var indicator = JsonSerializer.Deserialize<TechnicalIndicator>(json!);
                    if (indicator != null)
                    {
                        results.Add(indicator);
                    }
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
                var historyKey = $"{HistoryKeyPrefix}{code}:{dateTime:yyyyMMddHHmmss}";
                var json = JsonSerializer.Serialize(indicators);

                tasks.Add(db.StringSetAsync(key, json, TimeSpan.FromHours(24)));
                tasks.Add(db.StringSetAsync(historyKey, json, TimeSpan.FromDays(30)));
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
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var pattern = $"{HistoryKeyPrefix}*";
            var cutoff = DateTime.UtcNow - maxAge;

            var deletedCount = 0;
            foreach (var key in server.Keys(pattern: pattern))
            {
                var keyStr = key.ToString();
                var dateStr = keyStr.Split(':').Last();
                if (DateTime.TryParseExact(dateStr, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    if (date < cutoff)
                    {
                        await db.KeyDeleteAsync(key);
                        deletedCount++;
                    }
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
