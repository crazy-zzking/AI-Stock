using System.Text.Json;
using System.Text.Json.Serialization;
using AIStock.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Services;

/// <summary>
/// 交易日历服务 — 调用外部交易日接口，带内存缓存（当天有效）
/// </summary>
public class TradingCalendarService : ITradingCalendar
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TradingCalendarService> _logger;

    private const string WorkdayUrl = "http://115.29.178.22:8080/api/workday/range";
    private const string CacheKeyPrefix = "trading_calendar";

    public TradingCalendarService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<TradingCalendarService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsTradingDayAsync(DateTime date, CancellationToken ct = default)
    {
        var days = await FetchTradingDaysAsync(date.AddDays(-1), date, ct);
        return days.Contains(date.Date);
    }

    public async Task<HashSet<DateTime>> FetchTradingDaysAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var key = $"{CacheKeyPrefix}:{start:yyyyMMdd}:{end:yyyyMMdd}";
        if (_cache.TryGetValue<HashSet<DateTime>>(key, out var cached) && cached != null)
            return cached;

        var result = await FetchFromApiAsync(start, end, ct);

        // 缓存到当天 16:00（收盘后刷新）
        var expireAt = DateTime.Today.AddHours(16);
        if (DateTime.Now > expireAt) expireAt = expireAt.AddDays(1);
        _cache.Set(key, result, expireAt);

        return result;
    }

    private async Task<HashSet<DateTime>> FetchFromApiAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("default");
            var url = $"{WorkdayUrl}?start={start:yyyyMMdd}&end={end:yyyyMMdd}";
            var json = await client.GetStringAsync(url, ct);
            var resp = JsonSerializer.Deserialize<WorkdayResponse>(json);
            var list = resp?.Data?.List;
            if (list == null) return Fallback();

            return list
                .Where(x => !string.IsNullOrEmpty(x.Numeric))
                .Select(x => DateTime.TryParseExact(x.Numeric, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var d) ? (DateTime?)d : null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value.Date)
                .ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "交易日接口获取失败，降级为周末判断");
            return Fallback();
        }
    }

    /// <summary>降级：周末不算交易日</summary>
    private static HashSet<DateTime> Fallback()
    {
        var set = new HashSet<DateTime>();
        for (var d = DateTime.Today.AddDays(-30); d <= DateTime.Today.AddDays(30); d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                set.Add(d);
        }
        return set;
    }
}

// ---- 交易日接口响应模型 ----

public class WorkdayResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public WorkdayData? Data { get; set; }
}

public class WorkdayData
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("list")] public List<WorkdayItem>? List { get; set; }
}

public class WorkdayItem
{
    [JsonPropertyName("iso")] public string? Iso { get; set; }
    [JsonPropertyName("numeric")] public string? Numeric { get; set; }
}
