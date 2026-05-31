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
    public void Screen_VolumeSurge_HitsFeature()
    {
        var pool = ActivityScreener.Screen(new[] { Snap("A", change: 6m, volRatio: 2m) }, Default);

        Assert.Single(pool);
        Assert.Contains("放量大涨", pool[0].Features);
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
