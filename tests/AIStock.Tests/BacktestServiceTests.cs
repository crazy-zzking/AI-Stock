using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Backtest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStock.Tests;

/// <summary>
/// 回测编排测试（EF InMemory）：从 selection_result 历史取信号 + kline_data → 回测报告。
/// </summary>
public class BacktestServiceTests
{
    private static readonly DateTime Base = new(2026, 1, 5);

    private static AIStockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AIStockDbContext>()
            .UseInMemoryDatabase($"bt-svc-{Guid.NewGuid()}")
            .Options);

    private static BacktestService NewSvc(AIStockDbContext db) =>
        new(db, NullLogger<BacktestService>.Instance);

    private static void AddKline(AIStockDbContext db, ref long id, string code, int day,
        decimal o, decimal h, decimal l, decimal c) =>
        db.KlineData.Add(new KlineDataEntity
        {
            Id = id++, Code = code, Interval = nameof(KlineInterval.Daily),
            DateTime = Base.AddDays(day), Open = o, High = h, Low = l, Close = c, Source = "test",
        });

    [Fact]
    public async Task BacktestHistory_FromSelectionResults_ComputesReport()
    {
        using var db = NewDb();

        var picks = new List<StockSelectionResult> { new() { Code = "A", Name = "测试A" } };
        db.SelectionResult.Add(new SelectionResultEntity
        {
            TradingDate = Base, RunAt = Base.AddHours(17), TopN = 1,
            ResultsJson = JsonSerializer.Serialize(picks),
        });

        long id = 1;
        AddKline(db, ref id, "A", 0, 9, 9, 9, 9);        // 信号日
        AddKline(db, ref id, "A", 1, 10, 10, 10, 10);    // T+1 entry open 10
        AddKline(db, ref id, "A", 2, 10, 11, 10, 10.5m);
        AddKline(db, ref id, "A", 3, 10, 11, 10, 10.5m);
        AddKline(db, ref id, "A", 4, 10, 11, 10, 10.5m);
        AddKline(db, ref id, "A", 5, 10, 12, 10, 11);
        AddKline(db, ref id, "A", 6, 11, 13, 11, 12);    // exit close 12 → +20%
        await db.SaveChangesAsync();

        var report = await NewSvc(db).BacktestHistoryAsync(new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

        Assert.Equal(1, report.ExecutedTrades);
        Assert.Equal(20m, report.Trades[0].ReturnPct);
        Assert.Equal(100m, report.WinRatePct);
    }

    [Fact]
    public async Task BacktestHistory_NoSignals_ReturnsEmptyReport()
    {
        using var db = NewDb();
        var report = await NewSvc(db).BacktestHistoryAsync(new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

        Assert.Equal(0, report.TotalSignals);
        Assert.Equal(0, report.ExecutedTrades);
    }

    [Fact]
    public async Task BacktestHistory_DedupsSameStockSameDay()
    {
        using var db = NewDb();

        // 同一交易日两条记录都含 A → 只计一次信号
        var picks = new List<StockSelectionResult> { new() { Code = "A", Name = "A" } };
        for (int i = 0; i < 2; i++)
            db.SelectionResult.Add(new SelectionResultEntity
            {
                TradingDate = Base, RunAt = Base.AddHours(17 + i), TopN = 1,
                ResultsJson = JsonSerializer.Serialize(picks),
            });

        long id = 1;
        AddKline(db, ref id, "A", 0, 9, 9, 9, 9);
        AddKline(db, ref id, "A", 1, 10, 10, 10, 10);
        AddKline(db, ref id, "A", 2, 10, 11, 10, 11);
        await db.SaveChangesAsync();

        var report = await NewSvc(db).BacktestHistoryAsync(new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

        Assert.Equal(1, report.TotalSignals); // 去重后仅 1 个信号
    }
}
