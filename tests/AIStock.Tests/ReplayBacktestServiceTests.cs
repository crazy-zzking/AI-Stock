using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Backtest;
using AIStock.Selection.Narration;
using AIStock.Selection.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStock.Tests;

/// <summary>
/// 参数回放回测测试（EF InMemory）：历史快照逐日重跑选股 → 信号 → 回测。
/// </summary>
public class ReplayBacktestServiceTests
{
    private static readonly DateTime D1 = new(2026, 1, 5);
    private static readonly DateTime D2 = new(2026, 1, 6);

    private static AIStockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AIStockDbContext>()
            .UseInMemoryDatabase($"replay-{Guid.NewGuid()}")
            .Options);

    private static ReplayBacktestService NewSvc(AIStockDbContext db)
    {
        var engine = new StockSelectionEngine(new RuleLogicNarrator());
        var strategies = new ISelectionStrategy[]
        {
            new LowDipStrategy(engine), new TrendStrategy(), new ThemeStrategy(),
        };
        return new(db, strategies, NullLogger<ReplayBacktestService>.Instance);
    }

    // 温和放量 + 资金流入 + 低位未超买 → LowDip 会选中
    private static DailyMarketSnapshotEntity Snap(string code, DateTime date) => new()
    {
        Code = code, Name = code, Date = date,
        Close = 11m, ChangePercent = 5m, VolumeRatio = 2m,
        MainNetInflow = 50_000_000m, TotalMarketCap = 8_000_000_000m, PeTtm = 40m,
        Rise20d = 15m, Rsi = 60m, Ma5 = 10.8m, Ma10 = 10.4m, Ma20 = 10m,
        MacdGoldenCross = true, MacdDif = 1.1m, MacdDea = -0.03m, IsLimitUp = false,
    };

    private static void AddKline(AIStockDbContext db, ref long id, string code, DateTime d,
        decimal o, decimal h, decimal l, decimal c) =>
        db.KlineData.Add(new KlineDataEntity
        {
            Id = id++, Code = code, Interval = nameof(KlineInterval.Daily),
            DateTime = d, Open = o, High = h, Low = l, Close = c, Source = "test",
        });

    [Fact]
    public async Task ReplayBacktest_LowDip_ProducesSignalsAndReport()
    {
        using var db = NewDb();
        db.DailyMarketSnapshot.Add(Snap("A", D1));
        db.DailyMarketSnapshot.Add(Snap("A", D2));

        long id = 1;
        AddKline(db, ref id, "A", D1, 11, 11, 11, 11);
        AddKline(db, ref id, "A", D2, 11, 11.5m, 11, 11.3m);   // D1 信号 T+1 入场
        AddKline(db, ref id, "A", new DateTime(2026, 1, 7), 11.3m, 12, 11.3m, 12);
        AddKline(db, ref id, "A", new DateTime(2026, 1, 8), 12, 12.5m, 12, 12.4m);
        AddKline(db, ref id, "A", new DateTime(2026, 1, 9), 12.4m, 13, 12.4m, 12.8m);
        await db.SaveChangesAsync();

        var report = await NewSvc(db).BacktestParamsAsync(
            StrategyKeys.LowDip, new SelectionCriteria(), D1, D2,
            new BacktestConfig { HoldDays = 3 });

        Assert.Equal(2, report.TotalSignals);   // A 两个交易日各选一次
        Assert.True(report.ExecutedTrades >= 1);
    }

    [Fact]
    public async Task ReplayBacktest_DifferentParams_ChangeResult()
    {
        using var db = NewDb();
        db.DailyMarketSnapshot.Add(Snap("A", D1));
        long id = 1;
        AddKline(db, ref id, "A", D1, 11, 11, 11, 11);
        AddKline(db, ref id, "A", D2, 11, 11.5m, 11, 11.3m);
        AddKline(db, ref id, "A", new DateTime(2026, 1, 7), 11.3m, 12, 11.3m, 12);
        await db.SaveChangesAsync();

        // MaxRsi 调到 50：RSI 60 的 A 被超买过滤 → 无信号
        var strict = new SelectionCriteria { MaxRsi = 50m };
        var report = await NewSvc(db).BacktestParamsAsync(
            StrategyKeys.LowDip, strict, D1, D1, new BacktestConfig { HoldDays = 3 });

        Assert.Equal(0, report.TotalSignals); // 参数收紧后选不出 → 验证参数确实生效
    }

    [Fact]
    public async Task ReplayBacktest_NoSnapshots_ReturnsEmpty()
    {
        using var db = NewDb();
        var report = await NewSvc(db).BacktestParamsAsync(
            StrategyKeys.LowDip, new SelectionCriteria(), D1, D2, new BacktestConfig());
        Assert.Equal(0, report.TotalSignals);
    }
}
