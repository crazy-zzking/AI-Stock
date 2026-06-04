using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Feature.Services;
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
/// 参数回放回测测试（EF InMemory）：脱离 daily_market_snapshot，从 kline_data + daily_capital_flow
/// 重建历史快照后逐日重跑选股 → 回测。
/// </summary>
public class ReplayBacktestServiceTests
{
    private static readonly DateTime Base = new(2026, 1, 1);

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
        return new(db, strategies, new FeatureCalculatorService(), NullLogger<ReplayBacktestService>.Instance);
    }

    private static void AddKline(AIStockDbContext db, ref long id, string code, int day,
        decimal o, decimal h, decimal l, decimal c, long vol)
        => db.KlineData.Add(new KlineDataEntity
        {
            Id = id++, Code = code, Interval = nameof(KlineInterval.Daily),
            DateTime = Base.AddDays(day), Open = o, High = h, Low = l, Close = c,
            Volume = vol, Amount = c * vol, Source = "test",
        });

    /// <summary>造一只满足低吸选中的股票：长期平稳 + 第 24 日温和放量上涨，并补足回测所需后续K线。</summary>
    private static void SeedStock(AIStockDbContext db, string code)
    {
        db.StockBase.Add(new StockBaseEntity { Code = code, Name = code, IsDelisted = false });
        long id = (Math.Abs(code.GetHashCode()) % 100000) * 1000L + 1;
        for (var i = 0; i < 24; i++)
        {
            var c = i % 2 == 0 ? 10.05m : 9.95m;   // 小幅震荡(有涨有跌，避免 RSI 走极值)
            AddKline(db, ref id, code, i, c, c + 0.05m, c - 0.05m, c, 1_000_000);
        }
        AddKline(db, ref id, code, 24, 10m, 10.6m, 10m, 10.5m, 3_000_000);     // D1 温和放量上涨
        AddKline(db, ref id, code, 25, 10.5m, 11.1m, 10.5m, 11.0m, 3_000_000); // D2 续涨放量
        AddKline(db, ref id, code, 26, 11.0m, 11.5m, 11.0m, 11.3m, 2_000_000); // 回测持有期
        AddKline(db, ref id, code, 27, 11.3m, 11.8m, 11.2m, 11.6m, 2_000_000);
        AddKline(db, ref id, code, 28, 11.6m, 12.0m, 11.5m, 11.9m, 2_000_000);

        for (var i = 24; i <= 28; i++)
            db.CapitalFlow.Add(new CapitalFlowEntity { Code = code, Date = Base.AddDays(i), MainNetInflow = 50_000_000m });
    }

    [Fact]
    public async Task ReplayBacktest_RebuildsFromKline_ProducesSignals()
    {
        using var db = NewDb();
        SeedStock(db, "600111");
        await db.SaveChangesAsync();

        var report = await NewSvc(db).BacktestParamsAsync(
            StrategyKeys.LowDip, new SelectionCriteria(),
            Base.AddDays(24), Base.AddDays(25), new BacktestConfig { HoldDays = 3 });

        Assert.True(report.TotalSignals >= 1, "应从 K 线重建并选出至少一个信号");
        Assert.True(report.ExecutedTrades >= 1);
    }

    [Fact]
    public async Task ReplayBacktest_StrictRsi_NoSignals()
    {
        using var db = NewDb();
        SeedStock(db, "600111");
        await db.SaveChangesAsync();

        // MaxRsi 调到 1：任何股票都被超买过滤 → 无信号（验证参数确实驱动回放）
        var strict = new SelectionCriteria { MaxRsi = 1m };
        var report = await NewSvc(db).BacktestParamsAsync(
            StrategyKeys.LowDip, strict, Base.AddDays(24), Base.AddDays(24), new BacktestConfig { HoldDays = 3 });

        Assert.Equal(0, report.TotalSignals);
    }

    [Fact]
    public async Task ReplayBacktest_NoData_ReturnsEmpty()
    {
        using var db = NewDb();
        var report = await NewSvc(db).BacktestParamsAsync(
            StrategyKeys.LowDip, new SelectionCriteria(), Base.AddDays(24), Base.AddDays(25), new BacktestConfig());
        Assert.Equal(0, report.TotalSignals);
    }
}
