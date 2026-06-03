using AIStock.Selection.Backtest;

namespace AIStock.Tests;

/// <summary>
/// 统一回测引擎测试（纯计算）：成交价/收益/胜率/盈亏比/回撤/浮盈浮亏/数据缺失处理。
/// </summary>
public class BacktestEngineTests
{
    private static readonly DateTime Base = new(2026, 1, 5);

    private static BacktestBar Bar(int day, decimal o, decimal h, decimal l, decimal c)
        => new() { Date = Base.AddDays(day), Open = o, High = h, Low = l, Close = c };

    private static BacktestSignal Sig(string code, int day = 0)
        => new() { Code = code, Name = code, Date = Base.AddDays(day) };

    [Fact]
    public void Run_BasicTrade_ComputesEntryExitReturn()
    {
        // 信号日 day0(收盘9)，T+1 开盘10 买入，持有5个交易日到 day6 收盘12 → +20%
        var bars = new List<BacktestBar>
        {
            Bar(0, 9, 9, 9, 9),       // signal day
            Bar(1, 10, 10, 10, 10),   // entry (T+1 open=10)
            Bar(2, 10, 11, 10, 10.5m),
            Bar(3, 10.5m, 11, 9, 10), // 区间最低 9 → MaxDrop -10%
            Bar(4, 10, 11, 10, 10.5m),
            Bar(5, 10.5m, 12, 10, 11),
            Bar(6, 11, 13, 11, 12),   // exit (entry+5)，区间最高 13 → MaxRise +30%
            Bar(7, 12, 12, 12, 12),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("A") },
            new Dictionary<string, List<BacktestBar>> { ["A"] = bars },
            new BacktestConfig { HoldDays = 5 });

        Assert.Equal(1, report.ExecutedTrades);
        Assert.Equal(0, report.SkippedNoData);
        var t = report.Trades[0];
        Assert.Equal(10m, t.EntryPrice);
        Assert.Equal(12m, t.ExitPrice);
        Assert.Equal(5, t.HoldDays);
        Assert.Equal(20m, t.ReturnPct);
        Assert.Equal(30m, t.MaxRisePct);
        Assert.Equal(-10m, t.MaxDropPct);
        Assert.True(t.Win);
        Assert.Equal(100m, report.WinRatePct);
    }

    [Fact]
    public void Run_MissingBars_IsSkipped()
    {
        var report = BacktestEngine.Run(
            new[] { Sig("NOPE") },
            new Dictionary<string, List<BacktestBar>>(),
            new BacktestConfig { HoldDays = 5 });

        Assert.Equal(0, report.ExecutedTrades);
        Assert.Equal(1, report.SkippedNoData);
        Assert.Equal(1, report.TotalSignals);
    }

    [Fact]
    public void Run_WinAndLoss_ComputesWinRateAndProfitFactor()
    {
        // A：10→12 (+20%)；B：10→9 (-10%)。胜率50%，盈亏比=20/10=2，最大回撤=10
        List<BacktestBar> Series(decimal exitClose) => new()
        {
            Bar(0, 9, 9, 9, 9),
            Bar(1, 10, 10, 10, 10),
            Bar(2, 10, 10, 10, 10),
            Bar(3, 10, 10, 10, 10),
            Bar(4, 10, 10, 10, 10),
            Bar(5, 10, 10, 10, 10),
            Bar(6, exitClose, exitClose, exitClose, exitClose),
            Bar(7, exitClose, exitClose, exitClose, exitClose),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("A"), Sig("B") },
            new Dictionary<string, List<BacktestBar>> { ["A"] = Series(12m), ["B"] = Series(9m) },
            new BacktestConfig { HoldDays = 5 });

        Assert.Equal(2, report.ExecutedTrades);
        Assert.Equal(50m, report.WinRatePct);
        Assert.Equal(5m, report.AvgReturnPct);          // (20 + -10)/2
        Assert.Equal(2m, report.ProfitFactor);          // 20 / 10
        Assert.Equal(10m, report.MaxDrawdownPct);       // 累计 20 后回落到 10
    }

    [Fact]
    public void Run_InsufficientHold_ExitsAtLastBar()
    {
        // 只有 entry 后 2 根，holdDays=5 → 持有到最后一根
        var bars = new List<BacktestBar>
        {
            Bar(0, 9, 9, 9, 9),
            Bar(1, 10, 10, 10, 10),  // entry
            Bar(2, 10, 11, 10, 10.5m),
            Bar(3, 10.5m, 11, 10.5m, 11), // last
        };
        var report = BacktestEngine.Run(
            new[] { Sig("A") },
            new Dictionary<string, List<BacktestBar>> { ["A"] = bars },
            new BacktestConfig { HoldDays = 5 });

        var t = report.Trades[0];
        Assert.Equal(Base.AddDays(3), t.ExitDate);
        Assert.Equal(11m, t.ExitPrice);
        Assert.Equal(10m, t.ReturnPct); // (11-10)/10
        Assert.Equal(2, t.HoldDays);
    }

    [Fact]
    public void Run_SignalCloseEntry_BuysAtSignalClose()
    {
        var bars = new List<BacktestBar>
        {
            Bar(0, 9, 9, 9, 10),     // signal close = 10 → entry
            Bar(1, 10, 11, 10, 11),
            Bar(2, 11, 12, 11, 12),  // exit (entry+? holdDays=1 → day1)
        };
        var report = BacktestEngine.Run(
            new[] { Sig("A") },
            new Dictionary<string, List<BacktestBar>> { ["A"] = bars },
            new BacktestConfig { HoldDays = 1, Entry = BacktestEntryTiming.SignalClose });

        var t = report.Trades[0];
        Assert.Equal(10m, t.EntryPrice);     // 信号日收盘
        Assert.Equal(Base, t.EntryDate);
        Assert.Equal(11m, t.ExitPrice);      // 持有1日到 day1 收盘
        Assert.Equal(10m, t.ReturnPct);
    }
}
