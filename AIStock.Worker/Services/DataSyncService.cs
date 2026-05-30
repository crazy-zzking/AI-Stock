using System.Text.Json;
using System.Text.Json.Serialization;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Services;

/// <summary>
/// 数据同步服务 — 将股票池与日K落库到 stock_base / kline_data。
/// 股票池来自 DataSync:StockCodesUrl 接口，K线来自数据源 Provider。
/// </summary>
public class DataSyncService
{
    private readonly IDataProviderResolver _resolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DataSyncOptions _options;
    private readonly ILogger<DataSyncService> _logger;

    public DataSyncService(
        IDataProviderResolver resolver,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<DataSyncOptions> options,
        ILogger<DataSyncService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 从股票池接口获取全市场代码。
    /// </summary>
    private async Task<List<StockCodeDto>> FetchStockUniverseAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("default");
        var json = await client.GetStringAsync(_options.StockCodesUrl, ct);

        var resp = JsonSerializer.Deserialize<StockCodesResponse>(json);
        if (resp == null || resp.Code != 0 || resp.Data?.Codes == null)
        {
            _logger.LogWarning("股票池接口返回异常: code={Code}, message={Message}", resp?.Code, resp?.Message);
            return new List<StockCodeDto>();
        }

        return resp.Data.Codes;
    }

    /// <summary>
    /// 同步股票池基础信息到 stock_base（按代码 upsert）。返回 upsert 的股票数。
    /// </summary>
    public async Task<int> SyncStockBaseAsync(CancellationToken ct = default)
    {
        var stocks = await FetchStockUniverseAsync(ct);
        if (stocks.Count == 0)
        {
            _logger.LogWarning("股票池为空，跳过 stock_base 同步");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var existing = await db.StockBase.ToDictionaryAsync(s => s.Code, ct);
        var now = DateTime.UtcNow;
        var upserted = 0;

        foreach (var s in stocks)
        {
            if (string.IsNullOrEmpty(s.Code)) continue;

            var market = s.Exchange?.ToUpperInvariant() ?? string.Empty;

            if (existing.TryGetValue(s.Code, out var entity))
            {
                entity.Name = s.Name ?? entity.Name;
                entity.Market = market;
                entity.UpdatedAt = now;
            }
            else
            {
                db.StockBase.Add(new StockBaseEntity
                {
                    Code = s.Code,
                    Name = s.Name ?? string.Empty,
                    Market = market,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            upserted++;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("stock_base 同步完成：{Count} 只股票", upserted);
        return upserted;
    }

    /// <summary>
    /// 同步日K到 kline_data。仅插入比库内最新日期更新的K线，避免重复。
    /// </summary>
    public async Task<int> SyncKlinesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var codesQuery = db.StockBase.Where(s => !s.IsDelisted).OrderBy(s => s.Code).Select(s => s.Code);
        if (_options.MaxStocks > 0)
            codesQuery = codesQuery.Take(_options.MaxStocks);
        var codes = await codesQuery.ToListAsync(ct);

        if (codes.Count == 0)
        {
            _logger.LogWarning("stock_base 无股票，跳过K线同步（请先同步股票池）");
            return 0;
        }

        // K线统一用东方财富抓取
        var provider = _resolver.GetProviders(DataCapability.Kline)
            .FirstOrDefault(p => p.ProviderName == "东方财富");
        if (provider == null)
        {
            _logger.LogWarning("未找到东方财富数据源，跳过K线同步");
            return 0;
        }

        const string interval = nameof(KlineInterval.Daily);
        var totalInserted = 0;
        var processed = 0;

        _logger.LogInformation("开始K线同步：{Count} 只股票（数据源：东方财富）", codes.Count);

        foreach (var code in codes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // 库内该股票已有的最新日期，仅插入更新的部分
                var lastDate = await db.KlineData
                    .Where(k => k.Code == code && k.Interval == interval)
                    .MaxAsync(k => (DateTime?)k.DateTime, ct);

                var klines = await provider.GetKlinesAsync(code, KlineInterval.Daily, _options.KlineCount);
                if (klines == null || klines.Count == 0) continue;

                var fresh = klines
                    .Where(k => lastDate == null || k.DateTime > lastDate.Value)
                    .Select(k => new KlineDataEntity
                    {
                        Code = code,
                        DateTime = k.DateTime,
                        Interval = interval,
                        Open = k.Open,
                        Close = k.Close,
                        High = k.High,
                        Low = k.Low,
                        Volume = k.Volume,
                        Amount = k.Amount,
                        TurnoverRate = k.TurnoverRate,
                        ChangePercent = k.ChangePercent,
                        Source = provider.ProviderName,
                        CreatedAt = DateTime.UtcNow
                    })
                    .ToList();

                if (fresh.Count > 0)
                {
                    db.KlineData.AddRange(fresh);
                    await db.SaveChangesAsync(ct);
                    totalInserted += fresh.Count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "K线同步失败 {Code}", code);
            }

            if (++processed % 50 == 0)
                _logger.LogInformation("K线同步进度：{Processed}/{Total}，累计新增 {Rows} 条", processed, codes.Count, totalInserted);

            if (_options.KlineThrottleMs > 0)
                await Task.Delay(_options.KlineThrottleMs, ct);
        }

        _logger.LogInformation("kline_data 同步完成：{Stocks} 只股票，新增 {Rows} 条K线", codes.Count, totalInserted);
        return totalInserted;
    }
}

/// <summary>股票池接口响应</summary>
public class StockCodesResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public StockCodesData? Data { get; set; }
}

public class StockCodesData
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("codes")] public List<StockCodeDto>? Codes { get; set; }
}

public class StockCodeDto
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("exchange")] public string? Exchange { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}
