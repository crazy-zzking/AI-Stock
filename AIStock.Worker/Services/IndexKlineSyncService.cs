using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Worker.Services;

/// <summary>
/// 指数日 K 同步 — 拉取主要指数历史日K落库到 kline_data（以东财 secid 作 code，与6位个股代码不冲突）。
/// 供回放回测按日构造"截至当日"的大盘环境（无前视）。与实盘选股共用 RegimeEvaluator 的指数清单。
/// </summary>
public class IndexKlineSyncService
{
    private readonly IDataProviderResolver _resolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IndexKlineSyncService> _logger;

    public IndexKlineSyncService(
        IDataProviderResolver resolver, IServiceScopeFactory scopeFactory, ILogger<IndexKlineSyncService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 同步全部主要指数日K（upsert）。库中尚无该指数数据时拉取全部历史，已有则只拉最近 count 根做增量。
    /// 返回写入/更新的K线条数。
    /// </summary>
    public async Task<int> SyncAsync(int count = 10, CancellationToken ct = default)
    {
        var em = _resolver.GetProviders(DataCapability.Kline)
            .FirstOrDefault(p => p.ProviderName == "东方财富") as EastmoneyProvider;
        if (em == null)
        {
            _logger.LogWarning("指数K线同步：东方财富 Provider 不可用，跳过");
            return 0;
        }

        const string interval = nameof(KlineInterval.Daily);
        var total = 0;

        foreach (var (name, secid) in RegimeEvaluator.MarketIndices)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

                // 库中尚无该指数数据 → 首次全量回补；否则只拉最近 count 根增量
                var hasData = await db.KlineData
                    .AnyAsync(k => k.Code == secid && k.Interval == interval, ct);

                var kl = await em.GetIndexDailyAsync(secid, count, fullHistory: !hasData, ct: ct);
                if (kl.Count == 0)
                {
                    _logger.LogWarning("指数 {Name}({Secid}) 无K线返回", name, secid);
                    continue;
                }

                var minDate = kl.Min(k => k.DateTime).Date;
                var existing = await db.KlineData
                    .Where(k => k.Code == secid && k.Interval == interval && k.DateTime >= minDate)
                    .ToListAsync(ct);
                var byDate = existing.ToDictionary(k => k.DateTime.Date);

                foreach (var k in kl)
                {
                    if (byDate.TryGetValue(k.DateTime.Date, out var e))
                    {
                        e.Open = k.Open; e.Close = k.Close; e.High = k.High; e.Low = k.Low;
                        e.Volume = k.Volume; e.Amount = k.Amount;
                        e.TurnoverRate = k.TurnoverRate; e.ChangePercent = k.ChangePercent;
                    }
                    else
                    {
                        db.KlineData.Add(new KlineDataEntity
                        {
                            Code = secid, DateTime = k.DateTime, Interval = interval,
                            Open = k.Open, Close = k.Close, High = k.High, Low = k.Low,
                            Volume = k.Volume, Amount = k.Amount,
                            TurnoverRate = k.TurnoverRate, ChangePercent = k.ChangePercent,
                            Source = "eastmoney-index", CreatedAt = DateTime.Now,
                        });
                    }
                    total++;
                }

                await db.SaveChangesAsync(ct);
                _logger.LogInformation("指数 {Name}({Secid}) {Mode} {Cnt} 根日K",
                    name, secid, hasData ? "增量更新" : "全量回补", kl.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "指数 {Name}({Secid}) 同步失败", name, secid);
            }
        }

        _logger.LogInformation("指数K线同步完成，共处理 {Total} 条", total);
        return total;
    }
}
