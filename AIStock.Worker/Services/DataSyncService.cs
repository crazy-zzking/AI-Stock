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
/// 数据同步服务 — 将数据源的股票池与日K落库到 stock_base / kline_data。
/// </summary>
public class DataSyncService
{
    private readonly IDataProviderResolver _resolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataSyncOptions _options;
    private readonly ILogger<DataSyncService> _logger;

    public DataSyncService(
        IDataProviderResolver resolver,
        IServiceScopeFactory scopeFactory,
        IOptions<DataSyncOptions> options,
        ILogger<DataSyncService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 同步股票池基础信息到 stock_base（按代码 upsert）。返回 upsert 的股票数。
    /// </summary>
    public async Task<int> SyncStockBaseAsync(CancellationToken ct = default)
    {
        var provider = _resolver.GetDefaultProvider();
        var stocks = await provider.GetStockListAsync();
        if (stocks == null || stocks.Count == 0)
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

            if (existing.TryGetValue(s.Code, out var entity))
            {
                entity.Name = s.Name;
                entity.Market = s.Market;
                if (!string.IsNullOrEmpty(s.Industry)) entity.Industry = s.Industry;
                entity.ListDate = s.ListDate;
                entity.IsDelisted = s.IsDelisted;
                entity.UpdatedAt = now;
            }
            else
            {
                db.StockBase.Add(new StockBaseEntity
                {
                    Code = s.Code,
                    Name = s.Name,
                    Market = s.Market,
                    Industry = string.IsNullOrEmpty(s.Industry) ? null : s.Industry,
                    ListDate = s.ListDate,
                    IsDelisted = s.IsDelisted,
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

        var codesQuery = db.StockBase.Where(s => !s.IsDelisted).Select(s => s.Code);
        if (_options.MaxStocks > 0)
            codesQuery = codesQuery.Take(_options.MaxStocks);
        var codes = await codesQuery.ToListAsync(ct);

        if (codes.Count == 0)
        {
            _logger.LogWarning("stock_base 无股票，跳过K线同步（请先同步股票池）");
            return 0;
        }

        var provider = _resolver.GetDefaultProvider();
        const string interval = nameof(KlineInterval.Daily);
        var totalInserted = 0;

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

            if (_options.KlineThrottleMs > 0)
                await Task.Delay(_options.KlineThrottleMs, ct);
        }

        _logger.LogInformation("kline_data 同步完成：{Stocks} 只股票，新增 {Rows} 条K线", codes.Count, totalInserted);
        return totalInserted;
    }
}
