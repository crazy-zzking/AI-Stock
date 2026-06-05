using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection;

/// <summary>
/// 多日序列特征 —— 从近 N 日快照序列提炼的"过程"信号，弥补单日快照看不到趋势的缺陷。
/// </summary>
public record SequenceFeatures
{
    /// <summary>当前连续主力净流入天数（从最新往前数）</summary>
    public int ConsecutiveInflowDays { get; init; }
    /// <summary>近 5 日主力净流入为正的天数</summary>
    public int InflowDaysIn5 { get; init; }
    /// <summary>近 5 日累计主力净流入（元）</summary>
    public decimal CumNetInflow5 { get; init; }
    /// <summary>阶梯放量：近 3 日量比持续 ≥1 且不缩量</summary>
    public bool StairVolume { get; init; }
    /// <summary>突破近 20 日收盘新高</summary>
    public bool BreakoutNewHigh { get; init; }
    /// <summary>缩量回踩 MA10 企稳（上升中继）</summary>
    public bool PullbackStabilize { get; init; }
    /// <summary>近 10 日涨停次数</summary>
    public int LimitUpCountIn10 { get; init; }
    /// <summary>当前连续涨停板数（连板）</summary>
    public int ConsecutiveLimitUp { get; init; }
    /// <summary>近 5 日平均振幅（%），衡量波动/风险</summary>
    public decimal AvgAmplitude5 { get; init; }
}

/// <summary>
/// 序列分析器：输入某只股票按日期升序的快照序列，输出多日特征。纯计算，便于单测。
/// </summary>
public static class SequenceAnalyzer
{
    public static SequenceFeatures Analyze(IReadOnlyList<DailyMarketSnapshotEntity> asc)
    {
        if (asc.Count == 0) return new SequenceFeatures();

        var n = asc.Count;
        var last = asc[n - 1];

        // 连续主力净流入天数（从最新往前）
        var consecInflow = CountFromEnd(asc, x => x.MainNetInflow > 0);

        // 近 5 日资金
        var last5 = TakeLast(asc, 5);
        var inflow5 = last5.Count(x => x.MainNetInflow > 0);
        var cum5 = last5.Sum(x => x.MainNetInflow);

        // 近 5 日平均振幅（波动/风险）
        var amps5 = last5.Where(x => x.Amplitude > 0).Select(x => x.Amplitude).ToList();
        var avgAmp5 = amps5.Count > 0 ? Math.Round(amps5.Average(), 2) : 0m;

        // 阶梯放量：近 3 日量比均 ≥1 且最新不弱于 3 日前
        var stair = false;
        if (n >= 3)
        {
            var v = TakeLast(asc, 3).Select(x => x.VolumeRatio).ToList();
            stair = v.All(r => r >= 1m) && v[^1] >= v[0];
        }

        // 突破近 20 日收盘新高（最新 > 之前 20 日最高收盘）
        var breakout = false;
        if (n >= 2)
        {
            var prior = asc.Take(n - 1).ToList();
            var window = TakeLast(prior, 20);
            if (window.Count > 0) breakout = last.Close > window.Max(x => x.Close);
        }

        // 缩量回踩 MA10 企稳：价在 MA10 上方、近 3 日相对前期缩量、当日未大跌
        var pullback = false;
        if (n >= 6 && last.Ma10 > 0)
        {
            var recent3 = TakeLast(asc, 3).Average(x => x.VolumeRatio);
            var prev3 = asc.Skip(n - 6).Take(3).Average(x => x.VolumeRatio);
            pullback = last.Close >= last.Ma10 * 0.98m && recent3 < prev3 && last.ChangePercent > -3m;
        }

        // 涨停统计
        var limitUp10 = TakeLast(asc, 10).Count(x => x.IsLimitUp);
        var consecLimit = CountFromEnd(asc, x => x.IsLimitUp);

        return new SequenceFeatures
        {
            ConsecutiveInflowDays = consecInflow,
            InflowDaysIn5 = inflow5,
            CumNetInflow5 = cum5,
            StairVolume = stair,
            BreakoutNewHigh = breakout,
            PullbackStabilize = pullback,
            LimitUpCountIn10 = limitUp10,
            ConsecutiveLimitUp = consecLimit,
            AvgAmplitude5 = avgAmp5,
        };
    }

    private static int CountFromEnd(IReadOnlyList<DailyMarketSnapshotEntity> asc, Func<DailyMarketSnapshotEntity, bool> pred)
    {
        var c = 0;
        for (var i = asc.Count - 1; i >= 0; i--)
        {
            if (pred(asc[i])) c++;
            else break;
        }
        return c;
    }

    private static List<DailyMarketSnapshotEntity> TakeLast(IReadOnlyList<DailyMarketSnapshotEntity> asc, int k)
    {
        var start = Math.Max(0, asc.Count - k);
        var list = new List<DailyMarketSnapshotEntity>(asc.Count - start);
        for (var i = start; i < asc.Count; i++) list.Add(asc[i]);
        return list;
    }
}
