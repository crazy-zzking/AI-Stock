using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>
/// 多日序列特征分析测试（纯计算）。
/// </summary>
public class SequenceAnalyzerTests
{
    private static DailyMarketSnapshotEntity D(
        int day, decimal inflow = 0, decimal volRatio = 1m, decimal close = 10m,
        bool limitUp = false, decimal ma10 = 0m, decimal change = 0m) =>
        new()
        {
            Code = "A",
            Date = new DateTime(2026, 5, day),
            MainNetInflow = inflow,
            VolumeRatio = volRatio,
            Close = close,
            IsLimitUp = limitUp,
            Ma10 = ma10,
            ChangePercent = change,
        };

    [Fact]
    public void Empty_ReturnsZeros()
    {
        var f = SequenceAnalyzer.Analyze(Array.Empty<DailyMarketSnapshotEntity>());
        Assert.Equal(0, f.ConsecutiveInflowDays);
        Assert.False(f.BreakoutNewHigh);
    }

    [Fact]
    public void ConsecutiveInflow_CountsFromLatestBackward()
    {
        // 升序：第1天流出，后3天连续流入
        var seq = new[] { D(1, -100), D(2, 100), D(3, 100), D(4, 100) };
        var f = SequenceAnalyzer.Analyze(seq);
        Assert.Equal(3, f.ConsecutiveInflowDays);
        Assert.Equal(3, f.InflowDaysIn5);
        Assert.Equal(200, f.CumNetInflow5); // -100+100+100+100，含首日流出
    }

    [Fact]
    public void StairVolume_TrueWhenRising_FalseWhenShrinking()
    {
        var up = new[] { D(1, volRatio: 1.0m), D(2, volRatio: 1.3m), D(3, volRatio: 1.6m) };
        Assert.True(SequenceAnalyzer.Analyze(up).StairVolume);

        var down = new[] { D(1, volRatio: 1.6m), D(2, volRatio: 1.2m), D(3, volRatio: 0.8m) };
        Assert.False(SequenceAnalyzer.Analyze(down).StairVolume); // 末日 0.8 < 1
    }

    [Fact]
    public void BreakoutNewHigh_WhenLatestExceedsPriorMax()
    {
        var seq = new[] { D(1, close: 10), D(2, close: 11), D(3, close: 10.5m), D(4, close: 12) };
        Assert.True(SequenceAnalyzer.Analyze(seq).BreakoutNewHigh); // 12 > max(10,11,10.5)

        var noBreak = new[] { D(1, close: 10), D(2, close: 13), D(3, close: 12) };
        Assert.False(SequenceAnalyzer.Analyze(noBreak).BreakoutNewHigh); // 12 < 13
    }

    [Fact]
    public void LimitUp_CountsConsecutiveAndWindow()
    {
        var seq = new[] { D(1), D(2, limitUp: true), D(3, limitUp: true) };
        var f = SequenceAnalyzer.Analyze(seq);
        Assert.Equal(2, f.ConsecutiveLimitUp); // 末尾连续 2 板
        Assert.Equal(2, f.LimitUpCountIn10);

        // 中间断板：连续板数只数末尾
        var broken = new[] { D(1, limitUp: true), D(2), D(3, limitUp: true) };
        Assert.Equal(1, SequenceAnalyzer.Analyze(broken).ConsecutiveLimitUp);
        Assert.Equal(2, SequenceAnalyzer.Analyze(broken).LimitUpCountIn10);
    }

    [Fact]
    public void PullbackStabilize_WhenAboveMa10AndShrinkingVolume()
    {
        // 前期放量、近 3 日缩量、价在 MA10 上方、当日未大跌
        var seq = new[]
        {
            D(1, volRatio: 2.0m, close: 11), D(2, volRatio: 2.0m, close: 11.5m), D(3, volRatio: 1.8m, close: 11.2m),
            D(4, volRatio: 0.8m, close: 11.1m, ma10: 10.8m), D(5, volRatio: 0.7m, close: 11.0m, ma10: 10.9m),
            D(6, volRatio: 0.6m, close: 11.0m, ma10: 10.9m, change: -0.5m),
        };
        Assert.True(SequenceAnalyzer.Analyze(seq).PullbackStabilize);
    }

    /// <summary>带最高/最低/振幅的快照（用于守前低/振幅收敛特征）。</summary>
    private static DailyMarketSnapshotEntity DHL(
        int day, decimal close, decimal low, decimal amplitude = 3m, bool limitUp = false) =>
        new() { Code = "A", Date = new DateTime(2026, 5, day), Close = close, Low = low, High = close, Amplitude = amplitude, IsLimitUp = limitUp };

    private static DailyMarketSnapshotEntity DAmp(int day, decimal close, decimal amplitude) =>
        new() { Code = "A", Date = new DateTime(2026, 5, day), Close = close, Amplitude = amplitude };

    [Fact]
    public void HoldsStartLow_TrueWhenPullbackHoldsStartBarLow_FalseWhenBroken()
    {
        // 启动涨停(第2天,最低10.0) + 其后回踩：回踩低点 10.1 ≥ 启动低 10.0 → 守前低 true
        var hold = new[]
        {
            DHL(1, 9.5m, 9.3m), DHL(2, 10.5m, 10.0m, limitUp: true),
            DHL(3, 10.8m, 10.4m), DHL(4, 10.6m, 10.2m), DHL(5, 10.7m, 10.1m),
        };
        Assert.True(SequenceAnalyzer.Analyze(hold).HoldsStartLow);

        // 跌破启动低：回踩到 9.6 < 启动低 10.0*0.99 → false（突破失败/出货）
        var broken = new[]
        {
            DHL(1, 9.5m, 9.3m), DHL(2, 10.5m, 10.0m, limitUp: true),
            DHL(3, 10.2m, 9.9m), DHL(4, 9.9m, 9.6m), DHL(5, 9.8m, 9.6m),
        };
        Assert.False(SequenceAnalyzer.Analyze(broken).HoldsStartLow);
    }

    [Fact]
    public void HoldsStartLow_FalseWhenNoRecentLimitUp()
    {
        // 近期无涨停启动 → 无启动平台可守 → false
        var noStart = new[] { DHL(1, 10m, 9.8m), DHL(2, 10.1m, 9.9m), DHL(3, 10.2m, 10.0m) };
        Assert.False(SequenceAnalyzer.Analyze(noStart).HoldsStartLow);
    }

    [Fact]
    public void AmplitudeConverging_TrueWhenRecentNarrowerThanPrior()
    {
        // 振幅收敛：前3日均振幅 6%，近3日均振幅 2% → true
        var conv = new[]
        {
            DAmp(1, 11m, 6), DAmp(2, 11m, 6), DAmp(3, 11m, 6),
            DAmp(4, 11m, 2), DAmp(5, 11m, 2), DAmp(6, 11m, 2),
        };
        Assert.True(SequenceAnalyzer.Analyze(conv).AmplitudeConverging);

        // 振幅放大：前3日 2%，近3日 6% → false
        var diverge = new[]
        {
            DAmp(1, 11m, 2), DAmp(2, 11m, 2), DAmp(3, 11m, 2),
            DAmp(4, 11m, 6), DAmp(5, 11m, 6), DAmp(6, 11m, 6),
        };
        Assert.False(SequenceAnalyzer.Analyze(diverge).AmplitudeConverging);
    }
}
