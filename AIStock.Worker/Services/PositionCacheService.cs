using System.Text.Json;
using System.Text.Json.Serialization;
using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AIStock.Worker.Services;

/// <summary>
/// 持仓缓存服务 — 从 Sanhu 拉取持仓数据并写入 Redis，供 API 快速读取
/// </summary>
public class PositionCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ITradingCalendar _tradingCalendar;
    private readonly ILogger<PositionCacheService> _logger;

    private const string CacheKey = "aistock:cache:positions";
    private const string UpdatedKey = "aistock:cache:positions:updated";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = AIStock.Core.Json.AppJson.CjkEncoder
    };

    public PositionCacheService(
        IConnectionMultiplexer redis,
        IDataProviderResolver dataProviderResolver,
        ITradingCalendar tradingCalendar,
        ILogger<PositionCacheService> logger)
    {
        _redis = redis;
        _dataProviderResolver = dataProviderResolver;
        _tradingCalendar = tradingCalendar;
        _logger = logger;
    }

    /// <summary>
    /// 从 Sanhu 拉取最新持仓并写入 Redis 缓存
    /// </summary>
    public async Task RefreshCacheAsync(CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.Now; // 本地即北京时间

            // 非交易日 / 非交易时段一律跳过：休市时散户接口必然返回空，没必要调用，也避免日志刷屏
            if (!await _tradingCalendar.IsTradingDayAsync(now.Date, ct))
            {
                _logger.LogDebug("Not a trading day, skipping position cache refresh");
                return;
            }

            // A 股连续竞价时段（9:30-11:30, 13:00-15:00）才刷新
            if (!IsInTradingHours(now))
            {
                _logger.LogDebug("Outside trading hours ({Time}), skipping position cache refresh",
                    now.ToString("HH:mm"));
                return;
            }

            await FetchAndStoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh position cache");
        }
    }

    /// <summary>
    /// 判断当前时间是否在 A 股连续竞价时段（9:30-11:30, 13:00-15:00）
    /// </summary>
    internal static bool IsInTradingHours(DateTime beijingTime)
    {
        var t = beijingTime.TimeOfDay;
        return (t >= new TimeSpan(9, 30, 0) && t <= new TimeSpan(11, 30, 0))
            || (t >= new TimeSpan(13, 0, 0) && t <= new TimeSpan(15, 0, 0));
    }

    private async Task FetchAndStoreAsync()
    {
        var provider = _dataProviderResolver.GetPrimaryProvider(Core.Enums.DataCapability.Trading);
        if (provider == null)
        {
            _logger.LogWarning("No trading provider available for position cache refresh");
            return;
        }

        _logger.LogInformation("Fetching account info for position cache...");
        var accountInfo = await provider.GetAccountInfoAsync();
        if (accountInfo.Positions.Count == 0 && accountInfo.TotalAssets == 0)
        {
            _logger.LogWarning("Account info returned empty, skipping cache update");
            return;
        }

        var json = JsonSerializer.Serialize(accountInfo, JsonOptions);
        var db = _redis.GetDatabase();

        await db.StringSetAsync(CacheKey, json);
        await db.StringSetAsync(UpdatedKey, DateTime.Now.ToString("O"));

        _logger.LogInformation(
            "Position cache updated: TotalAssets={TotalAssets}, Positions={Count}",
            accountInfo.TotalAssets, accountInfo.Positions.Count);
    }

    /// <summary>
    /// 从 Redis 读取缓存的持仓数据
    /// </summary>
    public static async Task<AccountInfo?> GetCachedPositionsAsync(IConnectionMultiplexer redis)
    {
        try
        {
            var db = redis.GetDatabase();
            var json = await db.StringGetAsync(CacheKey);
            if (json.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<AccountInfo>(json!, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取缓存更新时间
    /// </summary>
    public static async Task<DateTime?> GetCacheUpdatedAtAsync(IConnectionMultiplexer redis)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync(UpdatedKey);
            if (value.IsNullOrEmpty)
                return null;

            return DateTime.Parse(value!);
        }
        catch
        {
            return null;
        }
    }
}
