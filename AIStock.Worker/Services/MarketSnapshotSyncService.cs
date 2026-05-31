using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Services;

/// <summary>
/// 市场快照采集服务 — 收盘后遍历股票池，聚合 行情/估值/资金流/技术指标，
/// 按 (code, date) upsert 到 daily_market_snapshot，作为选股引擎的数据源。
/// </summary>
public class MarketSnapshotSyncService
{
    private readonly IDataProviderResolver _resolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MarketSnapshotOptions _options;
    private readonly ILogger<MarketSnapshotSyncService> _logger;

    public MarketSnapshotSyncService(
        IDataProviderResolver resolver,
        IServiceScopeFactory scopeFactory,
        IOptions<MarketSnapshotOptions> options,
        ILogger<MarketSnapshotSyncService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>遍历股票池采集当日快照，返回成功落库条数。</summary>
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var provider = _resolver.GetDefaultProvider();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var featureCalculator = scope.ServiceProvider.GetRequiredService<IFeatureCalculator>();

        var query = db.StockBase.Where(s => !s.IsDelisted).Select(s => new { s.Code, s.Name });
        if (_options.MaxStocks > 0) query = query.Take(_options.MaxStocks);
        var stocks = await query.ToListAsync(ct);

        if (stocks.Count == 0)
        {
            _logger.LogWarning("股票池为空，跳过市场快照采集");
            return 0;
        }

        _logger.LogInformation("开始采集市场快照，共 {Count} 只", stocks.Count);
        var ok = 0;
        foreach (var stock in stocks)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var snap = await BuildSnapshotAsync(provider, featureCalculator, stock.Code, stock.Name, ct);
                if (snap != null)
                {
                    await UpsertAsync(db, snap, ct);
                    ok++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "快照采集失败：{Code}", stock.Code);
            }

            if (_options.ItemThrottleMs > 0)
                await Task.Delay(_options.ItemThrottleMs, ct);
        }

        _logger.LogInformation("市场快照采集完成：{Ok}/{Total}", ok, stocks.Count);
        return ok;
    }

    private static async Task<DailyMarketSnapshotEntity?> BuildSnapshotAsync(
        IDataProvider provider, IFeatureCalculator featureCalculator, string code, string name, CancellationToken ct)
    {
        var klines = await provider.GetKlinesAsync(code, KlineInterval.Daily, 30);
        if (klines.Count < 21) return null; // 不足 21 根无法算 20 日涨幅

        var ordered = klines.OrderBy(k => k.DateTime).ToList();
        var last = ordered[^1];
        var prevClose = ordered.Count >= 2 ? ordered[^2].Close : last.Open;
        var close20Ago = ordered[^21].Close;

        var ma = featureCalculator.CalculateMA(ordered);
        var macd = featureCalculator.CalculateMACD(ordered);
        var rsi = featureCalculator.CalculateRSI(ordered);

        // 估值/资金流为外部增量接口，失败不阻断（取不到则为 0）
        QuoteData? quote = null;
        CapitalFlowData? flow = null;
        try { quote = await provider.GetQuoteAsync(code); } catch { /* 容错 */ }
        try { flow = await provider.GetCapitalFlowAsync(code); } catch { /* 容错 */ }

        var changePercent = quote?.ChangePercent
            ?? (prevClose > 0 ? (last.Close - prevClose) / prevClose * 100 : 0);
        var rise20d = close20Ago > 0 ? (last.Close - close20Ago) / close20Ago * 100 : 0;
        var amplitude = prevClose > 0 ? (last.High - last.Low) / prevClose * 100 : 0;
        var limitThreshold = LimitUpThreshold(code);
        var isLimitUp = changePercent >= limitThreshold - 0.3m; // 留 0.3% 容差

        // MACD 金叉初期：DIF 上穿 DEA 且刚由负转正附近
        var goldenCross = macd.DIF > macd.DEA && macd.DIF > -0.05m;

        return new DailyMarketSnapshotEntity
        {
            Code = code,
            Name = name,
            Date = last.DateTime.Date,
            Close = last.Close,
            ChangePercent = changePercent,
            TurnoverRate = quote?.TurnoverRate ?? last.TurnoverRate,
            VolumeRatio = quote?.VolumeRatio ?? 0,
            Amplitude = amplitude,
            IsLimitUp = isLimitUp,
            TotalMarketCap = quote?.TotalMarketCap ?? 0,
            PeTtm = quote?.PeTtm ?? 0,
            MainNetInflow = flow?.MainNetInflow ?? 0,
            Rise20d = rise20d,
            Ma5 = ma.MA5 ?? 0,
            Ma10 = ma.MA10 ?? 0,
            Ma20 = ma.MA20 ?? 0,
            MacdDif = macd.DIF,
            MacdDea = macd.DEA,
            MacdGoldenCross = goldenCross,
            Rsi = rsi.RSI12
        };
    }

    /// <summary>涨停阈值（%）：创业板/科创板 20，北交所 30，其余 10。</summary>
    private static decimal LimitUpThreshold(string code) =>
        code.StartsWith("300") || code.StartsWith("688") ? 20m :
        code.StartsWith("8") || code.StartsWith("4") ? 30m : 10m;

    private static async Task UpsertAsync(AIStockDbContext db, DailyMarketSnapshotEntity snap, CancellationToken ct)
    {
        var existing = await db.DailyMarketSnapshot
            .FirstOrDefaultAsync(s => s.Code == snap.Code && s.Date == snap.Date, ct);

        if (existing == null)
        {
            db.DailyMarketSnapshot.Add(snap);
        }
        else
        {
            existing.Name = snap.Name;
            existing.Close = snap.Close;
            existing.ChangePercent = snap.ChangePercent;
            existing.TurnoverRate = snap.TurnoverRate;
            existing.VolumeRatio = snap.VolumeRatio;
            existing.Amplitude = snap.Amplitude;
            existing.IsLimitUp = snap.IsLimitUp;
            existing.TotalMarketCap = snap.TotalMarketCap;
            existing.PeTtm = snap.PeTtm;
            existing.MainNetInflow = snap.MainNetInflow;
            existing.Rise20d = snap.Rise20d;
            existing.Ma5 = snap.Ma5;
            existing.Ma10 = snap.Ma10;
            existing.Ma20 = snap.Ma20;
            existing.MacdDif = snap.MacdDif;
            existing.MacdDea = snap.MacdDea;
            existing.MacdGoldenCross = snap.MacdGoldenCross;
            existing.Rsi = snap.Rsi;
        }

        await db.SaveChangesAsync(ct);
    }
}
