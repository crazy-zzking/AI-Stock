using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Worker.Services;

/// <summary>
/// 每日资金流同步 — 拉东财资金流(fflow/daykline)落库到 capital_flow，供回放回测补资金面(kline_data 无资金流)。
/// 增量模式：库中已有该票数据则只拉最近 IncrementalDays 根，无数据(首次)才拉全历史——避免每日重拉全市场全历史。
/// 仅插入库内缺失日期(资金流历史不变，不重复更新)。全市场逐股请求，分批并发 + 批间延迟防封。
/// </summary>
public class CapitalFlowSyncService
{
    private const int BatchSize = 8;
    private const int BatchDelayMs = 200;

    /// <summary>增量回看根数：覆盖停牌/补数缓冲，远小于全历史(~数百根)。</summary>
    private const int IncrementalDays = 5;

    private readonly IDataProviderResolver _resolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CapitalFlowSyncService> _logger;

    public CapitalFlowSyncService(
        IDataProviderResolver resolver, IServiceScopeFactory scopeFactory, ILogger<CapitalFlowSyncService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>同步历史资金流。maxStocks>0 时仅处理前 N 只(调试/限量)。返回新增条数。</summary>
    public async Task<int> SyncAsync(int maxStocks = 0, CancellationToken ct = default)
    {
        var em = _resolver.GetProviders(DataCapability.Kline)
            .FirstOrDefault(p => p.ProviderName == "东方财富") as EastmoneyProvider;
        if (em == null)
        {
            _logger.LogWarning("资金流同步：东方财富 Provider 不可用，跳过");
            return 0;
        }

        List<string> codes;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var q = db.StockBase.Where(s => !s.IsDelisted).OrderBy(s => s.Code).Select(s => s.Code);
            codes = await (maxStocks > 0 ? q.Take(maxStocks) : q).ToListAsync(ct);
        }
        if (codes.Count == 0) { _logger.LogWarning("资金流同步：股票池为空"); return 0; }

        // 库中已有资金流数据的代码集合 → 这些走增量(只拉最近 N 根)，其余首次拉全历史
        HashSet<string> codesWithData;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            codesWithData = (await db.CapitalFlow.Select(c => c.Code).Distinct().ToListAsync(ct)).ToHashSet();
        }

        var added = 0;
        for (var i = 0; i < codes.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = codes.Skip(i).Take(BatchSize).ToList();
            var fetched = await Task.WhenAll(batch.Select(async c =>
            {
                var hasData = codesWithData.Contains(c);
                var flows = await em.GetCapitalFlowHistoryAsync(c, IncrementalDays, fullHistory: !hasData, ct);
                return (Code: c, Flows: flows);
            }));

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

            var batchCodes = batch;
            var existing = (await db.CapitalFlow
                    .Where(c => batchCodes.Contains(c.Code))
                    .Select(c => new { c.Code, c.Date })
                    .ToListAsync(ct))
                .GroupBy(x => x.Code)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Date.Date).ToHashSet());

            foreach (var (code, flows) in fetched)
            {
                existing.TryGetValue(code, out var have);
                foreach (var f in flows)
                {
                    if (have != null && have.Contains(f.Date.Date)) continue;
                    db.CapitalFlow.Add(new CapitalFlowEntity
                    {
                        Code = code, Date = f.Date.Date,
                        MainNetInflow = f.MainNetInflow,
                        SuperLargeNetInflow = f.SuperLargeNetInflow,
                        LargeNetInflow = f.LargeNetInflow,
                        MediumNetInflow = f.MediumNetInflow,
                        SmallNetInflow = f.SmallNetInflow,
                        CreatedAt = DateTime.Now,
                    });
                    added++;
                }
            }
            await db.SaveChangesAsync(ct);

            if (i + BatchSize < codes.Count)
                await Task.Delay(BatchDelayMs, ct);
        }

        _logger.LogInformation("资金流同步完成：{Codes} 只股票，新增 {Added} 条历史资金流", codes.Count, added);
        return added;
    }
}
