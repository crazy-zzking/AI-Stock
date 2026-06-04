using AIStock.Core.Models;
using AIStock.Feature.Services;
using AIStock.Selection.Backtest;

namespace AIStock.Tests;

/// <summary>K线重建快照测试（纯计算）：技术面/量比/均价/涨停从日K还原。</summary>
public class SnapshotRebuilderTests
{
    private static readonly DateTime Base = new(2026, 1, 1);

    [Fact]
    public void Rebuild_ComputesFieldsFromKlines()
    {
        var fc = new FeatureCalculatorService();
        var ks = new List<KlineData>();
        for (var i = 0; i < 24; i++)
            ks.Add(new KlineData { DateTime = Base.AddDays(i), Open = 10m, High = 10.1m, Low = 9.9m, Close = 10m, Volume = 1_000_000, Amount = 10_000_000m, TurnoverRate = 1m });
        // 当日：+5% 放量
        ks.Add(new KlineData { DateTime = Base.AddDays(24), Open = 10m, High = 10.6m, Low = 10m, Close = 10.5m, Volume = 3_000_000, Amount = 31_500_000m, TurnoverRate = 3m });

        var snap = SnapshotRebuilder.Rebuild("600999", "测试", ks, 5_000_000m, fc);

        Assert.NotNull(snap);
        Assert.Equal(10.5m, snap!.Close);
        Assert.InRange(snap.ChangePercent, 4.5m, 5.5m);   // (10.5-10)/10
        Assert.True(snap.VolumeRatio >= 1.5m);            // 300万 / 前5日均100万
        Assert.False(snap.IsLimitUp);                     // 主板 5% 未涨停
        Assert.Equal(10.5m, snap.AvgPrice);               // 3150万/300万
        Assert.Equal(5_000_000m, snap.MainNetInflow);
        Assert.True(snap.Ma5 > 0);
        Assert.Equal(0m, snap.TotalMarketCap);            // 历史市值不可得
    }

    [Fact]
    public void Rebuild_TooFewBars_ReturnsNull()
    {
        var fc = new FeatureCalculatorService();
        var ks = Enumerable.Range(0, 10)
            .Select(i => new KlineData { DateTime = Base.AddDays(i), Open = 10m, High = 10m, Low = 10m, Close = 10m, Volume = 1, Amount = 10m })
            .ToList();
        Assert.Null(SnapshotRebuilder.Rebuild("x", "x", ks, 0m, fc));
    }
}
