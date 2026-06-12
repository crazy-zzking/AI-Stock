using AIStock.Selection.Performance;

namespace AIStock.Tests;

/// <summary>
/// 选股信号前向绩效计算（纯函数）测试：T+1 开盘入场、T+1/3/5 收盘收益、
/// 一字板买不进判定（按板块涨停幅度/ST）、基准超额对齐、K线不足时的部分结果。
/// </summary>
public class PerformanceCalculatorTests
{
    private static readonly DateTime Signal = new(2026, 6, 1);

    /// <summary>从信号次日起连续生成日K（间隔1天，价格用传入序列）。</summary>
    private static List<PerfBar> Bars(params (decimal Open, decimal Close)[] days)
        => days.Select((d, i) => new PerfBar(Signal.AddDays(i + 1), d.Open, d.Close)).ToList();

    private static Dictionary<DateTime, PerfBar> IndexFlat(int days, decimal price = 4000m)
        => Enumerable.Range(1, days)
            .ToDictionary(i => Signal.AddDays(i), i => new PerfBar(Signal.AddDays(i), price, price));

    [Fact]
    public void Normal_FiveBars_ComputesAllHorizons_AndFinal()
    {
        // 入场 10.0，T+1 收 10.5(+5%)，T+3 收 11.0(+10%)，T+5 收 12.0(+20%)
        var bars = Bars((10.0m, 10.5m), (10.5m, 10.8m), (10.8m, 11.0m), (11.0m, 11.5m), (11.5m, 12.0m));
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "浦发银行", bars, IndexFlat(5));

        Assert.False(r.Untradable);
        Assert.True(r.Final);
        Assert.Equal(10.0m, r.EntryPrice);
        Assert.Equal(5.00m, r.Ret1);
        Assert.Equal(10.00m, r.Ret3);
        Assert.Equal(20.00m, r.Ret5);
        // 基准横盘 → 超额 = 自身收益
        Assert.Equal(5.00m, r.Excess1);
        Assert.Equal(20.00m, r.Excess5);
    }

    [Fact]
    public void OnlyTwoBars_PartialResult_NotFinal()
    {
        var bars = Bars((10.0m, 10.2m), (10.2m, 10.4m));
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "浦发银行", bars, IndexFlat(2));

        Assert.False(r.Final);
        Assert.Equal(2.00m, r.Ret1);
        Assert.Null(r.Ret3);
        Assert.Null(r.Ret5);
    }

    [Fact]
    public void NoBarsAfterSignal_StaysPending()
    {
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "浦发银行",
            new List<PerfBar>(), IndexFlat(0));

        Assert.Null(r.EntryDate);
        Assert.False(r.Final);
        Assert.False(r.Untradable);
    }

    [Fact]
    public void MainBoard_LimitUpOpen_Untradable()
    {
        // 主板 10%：昨收 10.0 → 涨停价 11.0，开盘 11.0 = 一字板买不进
        var bars = Bars((11.0m, 11.0m), (12.1m, 12.1m));
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "某主板", bars, IndexFlat(2));

        Assert.True(r.Untradable);
        Assert.True(r.Final);
        Assert.Null(r.Ret1);
    }

    [Fact]
    public void ChiNext_TenPercentOpen_TradableBecauseLimitIs20()
    {
        // 创业板 20%：昨收 10.0 → 涨停价 12.0，开盘 11.0（+10%）可以买
        var bars = Bars((11.0m, 11.5m));
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "300001", "某创业板", bars, IndexFlat(1));

        Assert.False(r.Untradable);
        Assert.Equal(11.0m, r.EntryPrice);
    }

    [Fact]
    public void ST_FivePercentOpen_Untradable()
    {
        // ST 5%：昨收 10.0 → 涨停价 10.5，开盘 10.5 = 一字板
        var bars = Bars((10.5m, 10.5m));
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600001", "ST某某", bars, IndexFlat(1));

        Assert.True(r.Untradable);
    }

    [Fact]
    public void IndexMissing_RetComputed_ExcessNull()
    {
        var bars = Bars((10.0m, 10.5m));
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "浦发银行",
            bars, new Dictionary<DateTime, PerfBar>());

        Assert.Equal(5.00m, r.Ret1);
        Assert.Null(r.Excess1);
    }

    [Fact]
    public void IndexRising_ExcessIsRetMinusBenchmark()
    {
        // 个股 +5%，指数同窗口 4000开 → 4080收(+2%) → 超额 +3 个百分点
        var bars = Bars((10.0m, 10.5m));
        var index = new Dictionary<DateTime, PerfBar>
        {
            [Signal.AddDays(1)] = new(Signal.AddDays(1), 4000m, 4080m),
        };
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "浦发银行", bars, index);

        Assert.Equal(5.00m, r.Ret1);
        Assert.Equal(3.00m, r.Excess1);
    }

    [Fact]
    public void SuspendedAfterSignal_PrevCloseRefreshedFromLastBarBeforeEntry()
    {
        // 信号日收 10.0，但信号日之前还有K线（含信号日自身收 10.2）→ 昨收以K线为准 10.2
        // 入场日开盘 11.22 = 10.2 × 1.10 → 一字板
        var bars = new List<PerfBar>
        {
            new(Signal, 10.0m, 10.2m),                 // 信号日K线
            new(Signal.AddDays(1), 11.22m, 11.22m),    // T+1 一字
        };
        var r = PerformanceCalculator.Compute(Signal, 10.0m, "600000", "某主板", bars, IndexFlat(2));

        Assert.True(r.Untradable);
    }

    [Theory]
    [InlineData("600000", "平安银行", 0.10)]
    [InlineData("000001", "平安银行", 0.10)]
    [InlineData("300750", "宁德时代", 0.20)]
    [InlineData("688981", "中芯国际", 0.20)]
    [InlineData("830001", "北交所股", 0.30)]
    [InlineData("430001", "北交所股", 0.30)]
    [InlineData("920001", "北交所股", 0.30)]
    [InlineData("600001", "ST海航", 0.05)]
    [InlineData("600002", "*ST凯撒", 0.05)]
    public void LimitUpRatio_ByBoardAndSt(string code, string name, decimal expected)
        => Assert.Equal(expected, PerformanceCalculator.LimitUpRatio(code, name));
}
