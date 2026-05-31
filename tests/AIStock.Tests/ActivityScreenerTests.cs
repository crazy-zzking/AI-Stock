using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>
/// 第一级漏斗：活跃度粗筛测试（纯计算，无需 DB）。
/// </summary>
public class ActivityScreenerTests
{
    private static DailyMarketSnapshotEntity Snap(
        string code, decimal change = 0, decimal volRatio = 0,
        decimal mainNet = 0, bool limitUp = false, decimal amplitude = 0) =>
        new()
        {
            Code = code,
            Name = code,
            Date = new DateTime(2026, 5, 12),
            ChangePercent = change,
            VolumeRatio = volRatio,
            MainNetInflow = mainNet,
            IsLimitUp = limitUp,
            Amplitude = amplitude
        };

    private static readonly SelectionCriteria Default = new();

    [Fact]
    public void Screen_HealthyVolume_HitsFeature()
    {
        // 温和放量上涨（未涨停、涨幅适中）是埋伏型首选信号
        var pool = ActivityScreener.Screen(new[] { Snap("A", change: 6m, volRatio: 2m) }, Default);

        Assert.Single(pool);
        Assert.Contains("温和放量", pool[0].Features);
    }

    [Fact]
    public void Screen_LimitUp_ScoresLowerThanHealthyVolume()
    {
        // 埋伏型：涨停仅作弱信号，活跃分应低于温和放量（规避次日高开追高）
        var limitUp = ActivityScreener.Screen(new[] { Snap("LU", change: 10m, volRatio: 3m, limitUp: true) }, Default);
        var healthy = ActivityScreener.Screen(new[] { Snap("HV", change: 5m, volRatio: 3m) }, Default);

        Assert.DoesNotContain("温和放量", limitUp[0].Features);
        Assert.Contains("涨停", limitUp[0].Features);
        Assert.True(healthy[0].ActivityScore > limitUp[0].ActivityScore);
    }

    [Fact]
    public void Screen_CapitalInflow_HitsFeature()
    {
        var pool = ActivityScreener.Screen(new[] { Snap("A", mainNet: 5_000_000m) }, Default);

        Assert.Single(pool);
        Assert.Contains("资金流入", pool[0].Features);
    }

    [Fact]
    public void Screen_LimitUp_HitsFeature()
    {
        var pool = ActivityScreener.Screen(new[] { Snap("A", limitUp: true) }, Default);

        Assert.Single(pool);
        Assert.Contains("涨停", pool[0].Features);
    }

    [Fact]
    public void Screen_VolatileShock_HitsFeature()
    {
        var pool = ActivityScreener.Screen(new[] { Snap("A", volRatio: 2m, amplitude: 7m) }, Default);

        Assert.Single(pool);
        Assert.Contains("放量震荡", pool[0].Features);
    }

    [Fact]
    public void Screen_Inactive_IsExcluded()
    {
        // 低涨幅、缩量、资金流出、未涨停、振幅小 → 无任何活跃特征
        var pool = ActivityScreener.Screen(
            new[] { Snap("A", change: 1m, volRatio: 0.5m, mainNet: -100m, amplitude: 2m) }, Default);

        Assert.Empty(pool);
    }

    [Fact]
    public void Screen_OrdersByActivityScoreDescending()
    {
        var weak = Snap("WEAK", mainNet: 1m);                       // 仅资金流入 25 分
        var strong = Snap("STRONG", change: 7m, volRatio: 3m,       // 放量大涨30+资金25+涨停30+震荡15
            mainNet: 1m, limitUp: true, amplitude: 8m);

        var pool = ActivityScreener.Screen(new[] { weak, strong }, Default);

        Assert.Equal("STRONG", pool[0].Snapshot.Code);
        Assert.True(pool[0].ActivityScore > pool[1].ActivityScore);
    }
}
