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
            new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

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
            new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

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
            new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

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
            new BacktestConfig { HoldDays = 5, ApplyTradability = false, FrictionPct = 0 });

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
            new BacktestConfig { HoldDays = 1, Entry = BacktestEntryTiming.SignalClose, ApplyTradability = false, FrictionPct = 0 });

        var t = report.Trades[0];
        Assert.Equal(10m, t.EntryPrice);     // 信号日收盘
        Assert.Equal(Base, t.EntryDate);
        Assert.Equal(11m, t.ExitPrice);      // 持有1日到 day1 收盘
        Assert.Equal(10m, t.ReturnPct);
    }

    // ---- 可成交性约束 + 交易摩擦 ----

    [Fact]
    public void Tradability_LimitUpOpen_SkippedUntradable()
    {
        // 主板 10%：昨收 10 → 涨停价 11，T+1 开盘 11 一字 → 买不进
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 11, 11, 11, 11),
            Bar(2, 12.1m, 12.1m, 12.1m, 12.1m),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 1, FrictionPct = 0 });

        Assert.Equal(0, report.ExecutedTrades);
        Assert.Equal(1, report.SkippedUntradable);
        Assert.Equal(0, report.SkippedNoData);
    }

    [Fact]
    public void Tradability_ChiNextTenPercentOpen_StillTradable()
    {
        // 创业板 20%：昨收 10 → 涨停价 12，开盘 11(+10%) 可以买
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 11, 11.5m, 11, 11.5m),
            Bar(2, 11.5m, 12, 11, 12),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("300001") },
            new Dictionary<string, List<BacktestBar>> { ["300001"] = bars },
            new BacktestConfig { HoldDays = 1, FrictionPct = 0 });

        Assert.Equal(1, report.ExecutedTrades);
        Assert.Equal(0, report.SkippedUntradable);
        Assert.Equal(11m, report.Trades[0].EntryPrice);
    }

    [Fact]
    public void Tradability_LimitDownOneWordExit_DefersToNextSellableDay()
    {
        // 买入 10；原定卖出日(day2) 一字跌停(9×0.9=8.1) → 顺延到 day3 收盘 8.5 成交
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10, 10, 9),            // entry open=10（未触涨停）
            Bar(2, 8.1m, 8.1m, 8.1m, 8.1m),   // 一字跌停（昨收9 → 跌停价8.1）
            Bar(3, 8.2m, 8.6m, 8, 8.5m),      // 可卖
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 1, FrictionPct = 0 });

        Assert.Equal(1, report.DeferredExits);
        var t = report.Trades[0];
        Assert.True(t.ExitDeferred);
        Assert.Equal(Base.AddDays(3), t.ExitDate);
        Assert.Equal(8.5m, t.ExitPrice);
        Assert.Equal(-15m, t.ReturnPct); // (8.5-10)/10
    }

    [Fact]
    public void Tradability_NonOneWordLimitDown_ExitsNormally()
    {
        // 卖出日触跌停但非一字（盘中有高于跌停的价）→ 视为可卖，不顺延
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10, 10, 9),
            Bar(2, 8.8m, 8.8m, 8.1m, 8.1m),  // 收在跌停但开盘 8.8 可卖
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 1, FrictionPct = 0 });

        Assert.Equal(0, report.DeferredExits);
        Assert.False(report.Trades[0].ExitDeferred);
        Assert.Equal(Base.AddDays(2), report.Trades[0].ExitDate);
    }

    [Fact]
    public void Friction_DeductedFromEachTradeReturn()
    {
        // 毛收益 +5%，摩擦 0.3 → 净 +4.7%
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10.5m, 10, 10.5m),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 1, FrictionPct = 0.3m });

        Assert.Equal(4.7m, report.Trades[0].ReturnPct);
        Assert.Equal(0.3m, report.FrictionPct);
    }

    [Fact]
    public void Tradability_Disabled_LimitUpOpenStillTrades()
    {
        // 关闭可成交性：一字涨停照样按开盘价成交（理想化对比口径）
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 11, 11, 11, 11),
            Bar(2, 11, 11, 11, 11),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 1, ApplyTradability = false, FrictionPct = 0 });

        Assert.Equal(1, report.ExecutedTrades);
        Assert.Equal(0, report.SkippedUntradable);
        Assert.Equal(11m, report.Trades[0].EntryPrice);
    }

    // ---- 规则出场（止损/止盈） ----

    [Fact]
    public void StopLoss_TriggersAtStopPrice()
    {
        // 买入 10，止损 8% → 止损价 9.2；day2 最低 9.0 触发 → 以 9.2 卖出
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10.2m, 9.8m, 10),     // entry open=10
            Bar(2, 9.8m, 9.9m, 9.0m, 9.5m),  // low 9.0 < 9.2 → 止损
            Bar(3, 9.5m, 11, 9.5m, 11),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 5, FrictionPct = 0, StopLossPct = 8m });

        var t = report.Trades[0];
        Assert.Equal("stoploss", t.ExitReason);
        Assert.Equal(9.2m, t.ExitPrice);
        Assert.Equal(Base.AddDays(2), t.ExitDate);
        Assert.Equal(-8m, t.ReturnPct);
    }

    [Fact]
    public void StopLoss_GapDownOpen_ExitsAtWorseOpen()
    {
        // 跳空低开 8.5 < 止损价 9.2 → 按开盘 8.5 成交（更差），不是理想化的 9.2
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10, 9.9m, 10),
            Bar(2, 8.5m, 8.8m, 8.4m, 8.6m),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 5, FrictionPct = 0, StopLossPct = 8m });

        Assert.Equal(8.5m, report.Trades[0].ExitPrice);
        Assert.Equal(-15m, report.Trades[0].ReturnPct);
    }

    [Fact]
    public void TakeProfit_TriggersAtTpPrice()
    {
        // 止盈 10% → 11.0；day2 最高 11.5 触发 → 以 11.0 卖出
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10.5m, 10, 10.4m),
            Bar(2, 10.5m, 11.5m, 10.4m, 11.2m),
            Bar(3, 11, 11, 10, 10),
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 5, FrictionPct = 0, TakeProfitPct = 10m });

        var t = report.Trades[0];
        Assert.Equal("takeprofit", t.ExitReason);
        Assert.Equal(11.0m, t.ExitPrice);
        Assert.Equal(10m, t.ReturnPct);
    }

    [Fact]
    public void StopLoss_OneWordLimitDown_CannotSell_DefersToNextDay()
    {
        // day2 一字跌停（昨收10→9.0）无法止损；day3 开盘 8.8 跳空成交
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10, 10, 10),          // entry，昨收10
            Bar(2, 9.0m, 9.0m, 9.0m, 9.0m),  // 一字跌停（10×0.9）低于止损价但卖不出
            Bar(3, 8.8m, 9.2m, 8.7m, 9.0m),  // 次日开盘 8.8 止损成交
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 5, FrictionPct = 0, StopLossPct = 8m });

        var t = report.Trades[0];
        Assert.Equal("stoploss", t.ExitReason);
        Assert.Equal(Base.AddDays(3), t.ExitDate);
        Assert.Equal(8.8m, t.ExitPrice);
    }

    [Fact]
    public void Rules_Disabled_BehavesAsTimeExit()
    {
        // 不配止损止盈：即使中途大跌也持有到期（旧行为）
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10, 10, 9.9m, 10),
            Bar(2, 9, 9, 8.5m, 8.6m),
            Bar(3, 8.6m, 10.5m, 8.6m, 10.5m), // exit (hold=2)
        };
        var report = BacktestEngine.Run(
            new[] { Sig("600001") },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 2, FrictionPct = 0 });

        Assert.Equal("time", report.Trades[0].ExitReason);
        Assert.Equal(10.5m, report.Trades[0].ExitPrice);
    }

    [Fact]
    public void Tradability_StSignal_FivePercentLimit()
    {
        // ST 5%：昨收 10 → 涨停价 10.5，开盘 10.5 → 买不进
        var bars = new List<BacktestBar>
        {
            Bar(0, 10, 10, 10, 10),
            Bar(1, 10.5m, 10.5m, 10.5m, 10.5m),
            Bar(2, 11, 11, 11, 11),
        };
        var sig = new BacktestSignal { Code = "600001", Name = "ST某某", Date = Base };
        var report = BacktestEngine.Run(
            new[] { sig },
            new Dictionary<string, List<BacktestBar>> { ["600001"] = bars },
            new BacktestConfig { HoldDays = 1, FrictionPct = 0 });

        Assert.Equal(1, report.SkippedUntradable);
    }
}
