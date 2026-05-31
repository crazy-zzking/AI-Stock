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
}
