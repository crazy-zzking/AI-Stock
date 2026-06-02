using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Data.Providers.Tencent;
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
        // 行情/估值/资金流统一用东财：GetDefaultProvider 返回的是散户(交易接口)，
        // 它对 quote/资金流返回空数据（市值/PE/资金流全 0 的根因）。
        var provider = _resolver.GetProviders(DataCapability.Quote)
            .FirstOrDefault(p => p.ProviderId == "eastmoney")
            ?? _resolver.GetDefaultProvider();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var featureCalculator = scope.ServiceProvider.GetRequiredService<IFeatureCalculator>();

        var query = db.StockBase.Where(s => !s.IsDelisted)
            .OrderBy(s => s.Code)
            .Select(s => new { s.Code, s.Name });
        if (_options.MaxStocks > 0) query = query.Take(_options.MaxStocks);
        var stocks = await query.ToListAsync(ct);

        if (stocks.Count == 0)
        {
            _logger.LogWarning("股票池为空，跳过市场快照采集");
            return 0;
        }

        _logger.LogInformation("开始采集市场快照，共 {Count} 只", stocks.Count);
        var ok = 0;
        var quoteFail = 0;
        var flowFail = 0;
        foreach (var stock in stocks)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var snap = await BuildSnapshotAsync(provider, featureCalculator, stock.Code, stock.Name, ct);
                if (snap != null)
                {
                    if (snap.TotalMarketCap == 0) quoteFail++; // quote 缺失（市值取不到）
                    if (snap.MainNetInflow == 0) flowFail++;   // 资金流缺失（近似，正好为0也计入）
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

        _logger.LogInformation("市场快照采集完成：{Ok}/{Total}（quote缺失 {QF}，资金流缺失 {FF}）",
            ok, stocks.Count, quoteFail, flowFail);
        return ok;
    }

    private const string DailyInterval = nameof(KlineInterval.Daily);

    /// <summary>
    /// 盘中快照采集 — 腾讯批量实时报价 + 东财批量主力净流入 + 库内历史日K，
    /// 拼当日成形K线算指标后批量 upsert 到 daily_market_snapshot（当天行）。支持盘中选股。
    /// </summary>
    public async Task<int> SyncIntradayAsync(CancellationToken ct = default)
    {
        var tencent = _resolver.GetProviders(DataCapability.Quote)
            .FirstOrDefault(p => p.ProviderId == "tencent") as TencentProvider;
        var eastmoney = _resolver.GetProviders(DataCapability.CapitalFlow)
            .FirstOrDefault(p => p.ProviderId == "eastmoney") as EastmoneyProvider;
        if (tencent == null)
        {
            _logger.LogWarning("未找到腾讯数据源，盘中快照跳过");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var featureCalculator = scope.ServiceProvider.GetRequiredService<IFeatureCalculator>();

        var query = db.StockBase.Where(s => !s.IsDelisted).OrderBy(s => s.Code).Select(s => new { s.Code, s.Name });
        if (_options.MaxStocks > 0) query = query.Take(_options.MaxStocks);
        var stocks = await query.ToListAsync(ct);
        if (stocks.Count == 0) { _logger.LogWarning("股票池为空，跳过盘中快照"); return 0; }

        var today = DateTime.Today;
        var since = today.AddDays(-90);
        var codes = stocks.Select(s => s.Code).ToList();
        var codeSet = codes.ToHashSet();

        // 1. 历史日K（截至昨日）→ code 分组（升序）
        var rows = await db.KlineData
            .Where(k => k.Interval == DailyInterval && k.DateTime >= since && k.DateTime < today)
            .Select(k => new { k.Code, k.DateTime, k.Open, k.Close, k.High, k.Low, k.Volume, k.Amount })
            .ToListAsync(ct);
        var klinesByCode = rows
            .Where(r => codeSet.Contains(r.Code))
            .GroupBy(r => r.Code)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DateTime)
                .Select(x => new KlineData { Code = x.Code, DateTime = x.DateTime, Open = x.Open, Close = x.Close, High = x.High, Low = x.Low, Volume = x.Volume, Amount = x.Amount })
                .ToList());

        // 2. 主力净流入（东财批量）
        Dictionary<string, decimal> flowByCode = new();
        if (eastmoney != null)
        {
            try { flowByCode = await eastmoney.GetMarketMainFlowAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "盘中主力净流入批量获取失败，资金面按 0"); }
        }

        // 3. 腾讯批量报价（分块并发）
        var quoteByCode = new System.Collections.Concurrent.ConcurrentDictionary<string, QuoteData>();
        var batch = Math.Max(10, _options.IntradayQuoteBatch);
        var chunks = new List<List<string>>();
        for (int i = 0; i < codes.Count; i += batch) chunks.Add(codes.Skip(i).Take(batch).ToList());
        using var sem = new SemaphoreSlim(8);
        await Task.WhenAll(chunks.Select(async chunk =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var quotes = await tencent.GetQuotesAsync(chunk);
                foreach (var q in quotes) if (!string.IsNullOrEmpty(q.Code)) quoteByCode[q.Code] = q;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "腾讯批量报价失败"); }
            finally { sem.Release(); }
        }));

        // 4. 组装快照
        var snaps = new List<DailyMarketSnapshotEntity>();
        foreach (var stock in stocks)
        {
            if (!klinesByCode.TryGetValue(stock.Code, out var hist) || hist.Count < 20) continue;
            if (!quoteByCode.TryGetValue(stock.Code, out var q) || q.Price <= 0) continue;

            var todayBar = new KlineData
            {
                Code = stock.Code, DateTime = today,
                Open = q.Open > 0 ? q.Open : q.Price, Close = q.Price,
                High = q.High > 0 ? q.High : q.Price, Low = q.Low > 0 ? q.Low : q.Price, Volume = q.Volume,
            };
            var ordered = new List<KlineData>(hist) { todayBar };
            if (ordered.Count < 21) continue;

            var ma = featureCalculator.CalculateMA(ordered);
            var macd = featureCalculator.CalculateMACD(ordered);
            var rsi = featureCalculator.CalculateRSI(ordered);

            var prevClose = q.PreClose > 0 ? q.PreClose : hist[^1].Close;
            var close20Ago = ordered[^21].Close;
            var rise20d = close20Ago > 0 ? (q.Price - close20Ago) / close20Ago * 100 : 0;
            var amplitude = prevClose > 0 ? (todayBar.High - todayBar.Low) / prevClose * 100 : 0;
            var changePercent = q.ChangePercent != 0 ? q.ChangePercent
                : (prevClose > 0 ? (q.Price - prevClose) / prevClose * 100 : 0);
            var isLimitUp = changePercent >= LimitUpThreshold(stock.Code) - 0.3m;
            var goldenCross = macd.DIF > macd.DEA && macd.DIF > -0.05m;

            snaps.Add(new DailyMarketSnapshotEntity
            {
                Code = stock.Code, Name = stock.Name, Date = today,
                Close = q.Price, ChangePercent = changePercent,
                TurnoverRate = q.TurnoverRate, VolumeRatio = q.VolumeRatio,
                Amplitude = amplitude, AvgPrice = q.AvgPrice, IsLimitUp = isLimitUp,
                TotalMarketCap = q.TotalMarketCap, PeTtm = q.PeTtm,
                MainNetInflow = flowByCode.TryGetValue(stock.Code, out var fl) ? fl : 0,
                Rise20d = rise20d,
                Ma5 = ma.MA5 ?? 0, Ma10 = ma.MA10 ?? 0, Ma20 = ma.MA20 ?? 0,
                MacdDif = macd.DIF, MacdDea = macd.DEA, MacdGoldenCross = goldenCross,
                Rsi = rsi.RSI12,
            });
        }

        // 5. 批量 upsert 当天行
        await UpsertBatchAsync(db, snaps, today, ct);
        _logger.LogInformation("盘中快照完成：{Ok}/{Total}（腾讯报价 {Q}，资金流 {F}）",
            snaps.Count, stocks.Count, quoteByCode.Count, flowByCode.Count);
        return snaps.Count;
    }

    private static async Task UpsertBatchAsync(AIStockDbContext db, List<DailyMarketSnapshotEntity> snaps, DateTime date, CancellationToken ct)
    {
        if (snaps.Count == 0) return;
        var codes = snaps.Select(s => s.Code).ToList();
        var existing = await db.DailyMarketSnapshot
            .Where(s => s.Date == date && codes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s, ct);

        foreach (var snap in snaps)
        {
            if (existing.TryGetValue(snap.Code, out var e))
            {
                e.Name = snap.Name; e.Close = snap.Close; e.ChangePercent = snap.ChangePercent;
                e.TurnoverRate = snap.TurnoverRate; e.VolumeRatio = snap.VolumeRatio; e.Amplitude = snap.Amplitude;
                e.IsLimitUp = snap.IsLimitUp; e.TotalMarketCap = snap.TotalMarketCap; e.PeTtm = snap.PeTtm;
                e.AvgPrice = snap.AvgPrice;
                e.MainNetInflow = snap.MainNetInflow; e.Rise20d = snap.Rise20d;
                e.Ma5 = snap.Ma5; e.Ma10 = snap.Ma10; e.Ma20 = snap.Ma20;
                e.MacdDif = snap.MacdDif; e.MacdDea = snap.MacdDea; e.MacdGoldenCross = snap.MacdGoldenCross; e.Rsi = snap.Rsi;
            }
            else db.DailyMarketSnapshot.Add(snap);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<DailyMarketSnapshotEntity?> BuildSnapshotAsync(
        IDataProvider provider, IFeatureCalculator featureCalculator, string code, string name, CancellationToken ct)
    {
        var klines = await provider.GetKlinesAsync(code, KlineInterval.Daily, 60);
        if (klines.Count < 21) return null; // 不足 21 根无法算 20 日涨幅（≥35 根 MACD 才收敛）

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
            AvgPrice = quote?.AvgPrice ?? 0,
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
            existing.AvgPrice = snap.AvgPrice;
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
