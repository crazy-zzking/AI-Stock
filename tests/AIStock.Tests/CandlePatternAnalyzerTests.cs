using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>
/// K 线形态识别测试（纯计算）。用构造的 OHLC 序列验证各形态识别正确。
/// </summary>
public class CandlePatternAnalyzerTests
{
    private static CandleBar B(decimal o, decimal h, decimal l, decimal c, long v = 1000)
        => new(o, h, l, c, v);

    /// <summary>生成一段平稳上涨的底仓 K 线（用于在末尾拼接待测形态，凑足均线/窗口长度）。</summary>
    private static List<CandleBar> Base(int count, decimal start = 10m, decimal step = 0m)
    {
        var list = new List<CandleBar>();
        var p = start;
        for (var i = 0; i < count; i++)
        {
            list.Add(B(p, p + 0.1m, p - 0.1m, p, 1000));
            p += step;
        }
        return list;
    }

    [Fact]
    public void Empty_ReturnsNoHits()
    {
        var f = CandlePatternAnalyzer.Analyze(Array.Empty<CandleBar>());
        Assert.False(f.Any);
        Assert.Empty(f.Hits);
    }

    [Fact]
    public void Hammer_LongLowerShadow_SmallBody_AtRecentLow()
    {
        var bars = Base(4, 12m, -0.5m); // 下行铺垫，末日探低
        // 锤子：长下影、小实体、短上影，最低创近 5 日新低
        bars.Add(B(10m, 10.2m, 8.5m, 10.05m));
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.Hammer);
        Assert.Contains(CandlePatternAnalyzer.Hammer, f.Hits);
    }

    [Fact]
    public void BullishEngulfing_PrevBearEngulfedByBull()
    {
        var bars = Base(3, 10m);
        bars.Add(B(10.5m, 10.6m, 9.9m, 10.0m)); // 阴线
        bars.Add(B(9.8m, 10.9m, 9.7m, 10.7m));  // 阳线吞没前阴实体
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.BullishEngulfing);
    }

    [Fact]
    public void Piercing_GapDownThenCloseAboveMidpoint()
    {
        var bars = Base(3, 10m);
        bars.Add(B(11.0m, 11.1m, 10.0m, 10.0m)); // 阴线 实体 10.0~11.0，中点 10.5
        bars.Add(B(9.9m, 10.8m, 9.8m, 10.7m));   // 低开 9.9(<前收10.0)，收 10.7(>中点，且<前开11.0)
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.Piercing);
    }

    [Fact]
    public void MorningStar_BearStarBull()
    {
        var bars = Base(3, 12m);
        bars.Add(B(12.0m, 12.1m, 11.0m, 11.1m));  // 大阴
        bars.Add(B(10.8m, 10.9m, 10.6m, 10.7m));  // 小星，跳空低于前收
        bars.Add(B(10.9m, 12.0m, 10.8m, 11.9m));  // 大阳，收回首根中点(11.55)上方
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.MorningStar);
    }

    [Fact]
    public void ThreeWhiteSoldiers_ThreeRisingBulls()
    {
        var bars = Base(3, 10m);
        bars.Add(B(10.0m, 10.55m, 9.95m, 10.5m)); // 阳
        bars.Add(B(10.3m, 11.05m, 10.25m, 11.0m)); // 阳，开在前实体内，收更高
        bars.Add(B(10.8m, 11.55m, 10.75m, 11.5m)); // 阳，开在前实体内，收更高
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.ThreeWhiteSoldiers);
    }

    [Fact]
    public void Doji_TinyBody()
    {
        var bars = Base(3, 10m);
        bars.Add(B(10.0m, 10.5m, 9.5m, 10.02m)); // 实体 0.02 << 振幅 1.0
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.Doji);
    }

    [Fact]
    public void SecondGoldenCross_RallyThenWashThenRestart()
    {
        // 构造：先涨一波（MA5 上穿 MA10）→ 回调洗盘（MA5 下穿 MA10）→ 再启动（MA5 再上穿 MA10）
        var bars = new List<CandleBar>();
        decimal p = 10m;
        // 0-9：横盘
        for (var i = 0; i < 10; i++) { bars.Add(B(p, p + 0.1m, p - 0.1m, p)); }
        // 10-19：大涨一波（+约20%）
        for (var i = 0; i < 10; i++) { p += 0.25m; bars.Add(B(p - 0.1m, p + 0.1m, p - 0.2m, p)); }
        // 20-28：高位回调（MA5 下穿 MA10）
        for (var i = 0; i < 9; i++) { p -= 0.18m; bars.Add(B(p + 0.1m, p + 0.15m, p - 0.1m, p)); }
        // 29-33：再启动（MA5 重新上穿 MA10）
        for (var i = 0; i < 5; i++) { p += 0.25m; bars.Add(B(p - 0.1m, p + 0.1m, p - 0.15m, p)); }

        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.SecondGoldenCross);
    }

    [Fact]
    public void BullishAlignment_MaStackedUp()
    {
        // 持续稳步上涨 → MA5>=MA10>=MA20 且收盘站上 MA5
        var bars = Base(25, 10m, 0.2m);
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.BullishAlignment);
    }

    [Fact]
    public void PlatformBreakout_BreakBoxHighWithBull()
    {
        var bars = new List<CandleBar>();
        // 10 日窄幅箱体（9.9~10.2）
        for (var i = 0; i < 11; i++) bars.Add(B(10.0m, 10.2m, 9.9m, 10.05m));
        // 当日阳线突破箱体上沿
        bars.Add(B(10.1m, 10.8m, 10.05m, 10.7m));
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.PlatformBreakout);
    }

    [Fact]
    public void VolumeBreakout_NewHighWithVolumeSurge()
    {
        var bars = new List<CandleBar>();
        for (var i = 0; i < 11; i++) bars.Add(B(10.0m, 10.2m, 9.9m, 10.0m, 1000));
        // 创近 10 日新高 + 放量（2 倍）
        bars.Add(B(10.1m, 10.9m, 10.05m, 10.8m, 2500));
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.VolumeBreakout);
    }

    [Fact]
    public void VolumeShrinkPullback_ShrinkingVolumeAboveMa20()
    {
        var bars = Base(20, 10m, 0.15m); // 站上 MA20 的上升趋势铺垫，前期常量
        // 近 3 日缩量 + 价格走平/小回
        var last = bars[^1].Close;
        bars.Add(B(last, last + 0.05m, last - 0.05m, last - 0.02m, 400));
        bars.Add(B(last, last + 0.05m, last - 0.05m, last - 0.04m, 350));
        bars.Add(B(last, last + 0.05m, last - 0.05m, last - 0.05m, 300));
        var f = CandlePatternAnalyzer.Analyze(bars);
        Assert.True(f.VolumeShrinkPullback);
    }
}
