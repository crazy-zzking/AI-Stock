namespace AIStock.Selection;

/// <summary>
/// K 线形态识别用的精简 OHLC 单根 K 线。纯值对象，便于单测构造。
/// </summary>
public readonly record struct CandleBar(decimal Open, decimal High, decimal Low, decimal Close, long Volume = 0)
{
    /// <summary>实体大小（|收-开|）</summary>
    public decimal Body => Math.Abs(Close - Open);
    /// <summary>振幅（高-低）</summary>
    public decimal Range => High - Low;
    /// <summary>上影线</summary>
    public decimal UpperShadow => High - Math.Max(Open, Close);
    /// <summary>下影线</summary>
    public decimal LowerShadow => Math.Min(Open, Close) - Low;
    public bool IsBull => Close > Open;
    public bool IsBear => Close < Open;
}

/// <summary>
/// 某只股票当日命中的 K 线形态集合。每个 bool 为一种形态;<see cref="Hits"/> 为命中形态的键列表。
/// </summary>
public record CandlePatternFeatures
{
    // —— A. 经典蜡烛形态（需 OHLC）——
    /// <summary>锤子线（底部反转）</summary>
    public bool Hammer { get; init; }
    /// <summary>看涨吞没</summary>
    public bool BullishEngulfing { get; init; }
    /// <summary>曙光初现</summary>
    public bool Piercing { get; init; }
    /// <summary>早晨之星</summary>
    public bool MorningStar { get; init; }
    /// <summary>红三兵</summary>
    public bool ThreeWhiteSoldiers { get; init; }
    /// <summary>十字星（企稳/变盘）</summary>
    public bool Doji { get; init; }

    // —— B. 趋势位置形态（用 MA + 收盘）——
    /// <summary>大涨后高位盘整再启动：MA5 下穿 MA10 后近期再上穿 MA10（二次金叉）</summary>
    public bool SecondGoldenCross { get; init; }
    /// <summary>均线多头排列</summary>
    public bool BullishAlignment { get; init; }
    /// <summary>平台/箱体突破</summary>
    public bool PlatformBreakout { get; init; }
    /// <summary>缩量回踩均线企稳</summary>
    public bool PullbackStabilize { get; init; }

    // —— C. 量价配合形态 ——
    /// <summary>放量突破</summary>
    public bool VolumeBreakout { get; init; }
    /// <summary>缩量回调</summary>
    public bool VolumeShrinkPullback { get; init; }

    /// <summary>命中形态的键列表（与 <see cref="CandlePatternAnalyzer.PatternKeys"/> 一致）。</summary>
    public IReadOnlyList<string> Hits { get; init; } = Array.Empty<string>();

    /// <summary>是否命中任意形态。</summary>
    public bool Any => Hits.Count > 0;
}

/// <summary>
/// K 线形态分析器：输入某只股票按日期升序的日 K（OHLC）序列，输出命中的形态。
/// 纯计算、无外部依赖，便于单测。阈值取业界常用默认值，可后续按偏好微调。
/// </summary>
public static class CandlePatternAnalyzer
{
    // 形态键（作为策略配置 RequirePatterns 的取值 / 展示映射键）
    public const string Hammer = "hammer";
    public const string BullishEngulfing = "engulfing";
    public const string Piercing = "piercing";
    public const string MorningStar = "morningstar";
    public const string ThreeWhiteSoldiers = "threesoldiers";
    public const string Doji = "doji";
    public const string SecondGoldenCross = "secondgoldencross";
    public const string BullishAlignment = "bullalignment";
    public const string PlatformBreakout = "platformbreakout";
    public const string PullbackStabilize = "pullbackstabilize";
    public const string VolumeBreakout = "volumebreakout";
    public const string VolumeShrinkPullback = "volumeshrink";

    /// <summary>全部形态键（顺序即展示/默认启用顺序）。</summary>
    public static readonly IReadOnlyList<string> PatternKeys = new[]
    {
        Hammer, BullishEngulfing, Piercing, MorningStar, ThreeWhiteSoldiers, Doji,
        SecondGoldenCross, BullishAlignment, PlatformBreakout, PullbackStabilize,
        VolumeBreakout, VolumeShrinkPullback,
    };

    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [Hammer] = "锤子线",
        [BullishEngulfing] = "看涨吞没",
        [Piercing] = "曙光初现",
        [MorningStar] = "早晨之星",
        [ThreeWhiteSoldiers] = "红三兵",
        [Doji] = "十字星",
        [SecondGoldenCross] = "二次金叉",
        [BullishAlignment] = "均线多头",
        [PlatformBreakout] = "平台突破",
        [PullbackStabilize] = "回踩企稳",
        [VolumeBreakout] = "放量突破",
        [VolumeShrinkPullback] = "缩量回调",
    };

    /// <summary>形态键 → 中文展示名（未知键原样返回）。</summary>
    public static string DisplayName(string key) => Names.TryGetValue(key, out var n) ? n : key;

    public static CandlePatternFeatures Analyze(IReadOnlyList<CandleBar> asc)
    {
        if (asc == null || asc.Count == 0) return new CandlePatternFeatures();

        var n = asc.Count;
        var last = asc[n - 1];
        var closes = new decimal[n];
        var volumes = new long[n];
        for (var i = 0; i < n; i++) { closes[i] = asc[i].Close; volumes[i] = asc[i].Volume; }

        var ma5 = Sma(closes, 5);
        var ma10 = Sma(closes, 10);
        var ma20 = Sma(closes, 20);

        var hammer = IsHammer(asc, n);
        var engulf = IsBullishEngulfing(asc, n);
        var piercing = IsPiercing(asc, n);
        var morning = IsMorningStar(asc, n);
        var soldiers = IsThreeWhiteSoldiers(asc, n);
        var doji = IsDoji(last);
        var secondGolden = IsSecondGoldenCross(closes, ma5, ma10, n);
        var bullAlign = ma5[n - 1] > 0 && ma10[n - 1] > 0 && ma20[n - 1] > 0
                        && ma5[n - 1] >= ma10[n - 1] && ma10[n - 1] >= ma20[n - 1] && last.Close >= ma5[n - 1];
        var platform = IsPlatformBreakout(asc, n);
        var pullback = IsPullbackStabilize(asc, ma10, volumes, n);
        var volBreak = IsVolumeBreakout(asc, closes, volumes, n);
        var volShrink = IsVolumeShrinkPullback(closes, ma20, volumes, n);

        var hits = new List<string>();
        if (hammer) hits.Add(Hammer);
        if (engulf) hits.Add(BullishEngulfing);
        if (piercing) hits.Add(Piercing);
        if (morning) hits.Add(MorningStar);
        if (soldiers) hits.Add(ThreeWhiteSoldiers);
        if (doji) hits.Add(Doji);
        if (secondGolden) hits.Add(SecondGoldenCross);
        if (bullAlign) hits.Add(BullishAlignment);
        if (platform) hits.Add(PlatformBreakout);
        if (pullback) hits.Add(PullbackStabilize);
        if (volBreak) hits.Add(VolumeBreakout);
        if (volShrink) hits.Add(VolumeShrinkPullback);

        return new CandlePatternFeatures
        {
            Hammer = hammer,
            BullishEngulfing = engulf,
            Piercing = piercing,
            MorningStar = morning,
            ThreeWhiteSoldiers = soldiers,
            Doji = doji,
            SecondGoldenCross = secondGolden,
            BullishAlignment = bullAlign,
            PlatformBreakout = platform,
            PullbackStabilize = pullback,
            VolumeBreakout = volBreak,
            VolumeShrinkPullback = volShrink,
            Hits = hits,
        };
    }

    // —— A. 经典蜡烛形态 ——

    /// <summary>锤子线：小实体、长下影(≥2倍实体)、短上影，且处于近期低点（底部反转）。</summary>
    private static bool IsHammer(IReadOnlyList<CandleBar> asc, int n)
    {
        var b = asc[n - 1];
        if (b.Range <= 0) return false;
        // 小实体、长下影(≥振幅 60%)、短上影(≤振幅 15%) —— 相对振幅衡量，避免实体极小时口径失真
        var smallBody = b.Body <= b.Range * 0.3m;
        var longLower = b.LowerShadow >= b.Range * 0.6m;
        var shortUpper = b.UpperShadow <= b.Range * 0.15m;
        if (!(smallBody && longLower && shortUpper)) return false;

        // 处于近期低点：当日最低是近 5 日最低（确认在底部探低后收回）
        var lookback = Math.Min(5, n);
        var minLow = decimal.MaxValue;
        for (var i = n - lookback; i < n; i++) minLow = Math.Min(minLow, asc[i].Low);
        return b.Low <= minLow + 0.0001m;
    }

    /// <summary>看涨吞没：前阴后阳，且阳线实体完全吞没前一阴线实体。</summary>
    private static bool IsBullishEngulfing(IReadOnlyList<CandleBar> asc, int n)
    {
        if (n < 2) return false;
        var prev = asc[n - 2];
        var cur = asc[n - 1];
        if (!prev.IsBear || !cur.IsBull) return false;
        if (prev.Body <= 0 || cur.Body <= 0) return false;
        return cur.Open <= prev.Close && cur.Close >= prev.Open;
    }

    /// <summary>曙光初现：前阴线后跳空低开的阳线，收盘上穿前阴线实体中点但未完全吞没。</summary>
    private static bool IsPiercing(IReadOnlyList<CandleBar> asc, int n)
    {
        if (n < 2) return false;
        var prev = asc[n - 2];
        var cur = asc[n - 1];
        if (!prev.IsBear || !cur.IsBull) return false;
        if (prev.Body <= 0) return false;
        var mid = (prev.Open + prev.Close) / 2m;
        return cur.Open < prev.Close && cur.Close > mid && cur.Close < prev.Open;
    }

    /// <summary>早晨之星：大阴线 → 小实体星线(跳空) → 大阳线收回首根实体中点上方。</summary>
    private static bool IsMorningStar(IReadOnlyList<CandleBar> asc, int n)
    {
        if (n < 3) return false;
        var a = asc[n - 3];
        var b = asc[n - 2];
        var c = asc[n - 1];
        if (a.Range <= 0 || c.Range <= 0) return false;
        var bigBear = a.IsBear && a.Body >= a.Range * 0.5m;
        var smallStar = b.Range > 0 && b.Body <= b.Range * 0.35m && Math.Max(b.Open, b.Close) < a.Close;
        var bigBull = c.IsBull && c.Body >= c.Range * 0.5m && c.Close >= (a.Open + a.Close) / 2m;
        return bigBear && smallStar && bigBull;
    }

    /// <summary>红三兵：连续 3 根阳线逐日收高，每根开盘落在前一根实体内、上影短。</summary>
    private static bool IsThreeWhiteSoldiers(IReadOnlyList<CandleBar> asc, int n)
    {
        if (n < 3) return false;
        var a = asc[n - 3];
        var b = asc[n - 2];
        var c = asc[n - 1];
        if (!(a.IsBull && b.IsBull && c.IsBull)) return false;
        if (!(b.Close > a.Close && c.Close > b.Close)) return false;
        // 每根开盘落在前一根实体内（不跳空过高），上影线不宜过长
        var withinB = b.Open >= a.Open && b.Open <= a.Close;
        var withinC = c.Open >= b.Open && c.Open <= b.Close;
        var shortUpper = b.UpperShadow <= b.Body && c.UpperShadow <= c.Body && a.UpperShadow <= a.Body;
        var solidBodies = a.Body > 0 && b.Body > 0 && c.Body > 0;
        return withinB && withinC && shortUpper && solidBodies;
    }

    /// <summary>十字星：实体极小（≤振幅 10%）。</summary>
    private static bool IsDoji(CandleBar b) => b.Range > 0 && b.Body <= b.Range * 0.1m;

    // —— B. 趋势位置形态 ——

    /// <summary>
    /// 二次金叉：前期 MA5 上穿 MA10 涨过一波 → 回调中 MA5 下穿 MA10（洗盘）→ 近 3 日 MA5 再次上穿 MA10（再启动）。
    /// 要求洗盘前确有涨幅（≥10%），即"大涨之后高位盘整又启动"。
    /// </summary>
    private static bool IsSecondGoldenCross(decimal[] closes, decimal[] ma5, decimal[] ma10, int n)
    {
        if (n < 25) return false;

        var ups = new List<int>();
        var downs = new List<int>();
        for (var i = 1; i < n; i++)
        {
            if (ma5[i] <= 0 || ma10[i] <= 0 || ma5[i - 1] <= 0 || ma10[i - 1] <= 0) continue;
            // 上穿：前一日 ≤、当日 >（含"由相等转大于"，避免横盘后启动的金叉被漏判，且与下穿互斥不重复）
            if (ma5[i - 1] <= ma10[i - 1] && ma5[i] > ma10[i]) ups.Add(i);
            else if (ma5[i - 1] >= ma10[i - 1] && ma5[i] < ma10[i]) downs.Add(i);
        }

        // 最近一次上穿发生在末 3 根内（再启动）
        var recentUp = ups.Where(i => i >= n - 3).DefaultIfEmpty(-1).Max();
        if (recentUp < 0) return false;

        // 再启动之前要有一次下穿（洗盘）
        var down = downs.Where(d => d < recentUp).DefaultIfEmpty(-1).Max();
        if (down < 0) return false;

        // 洗盘之前要有一次上穿（前期上涨的启动）
        var firstUp = ups.Where(u => u < down).DefaultIfEmpty(-1).Max();
        if (firstUp < 0) return false;

        // 前期确有涨幅（首次金叉到下穿之间最高收盘较首次金叉收盘涨 ≥10%）
        var peak = decimal.MinValue;
        for (var i = firstUp; i <= down; i++) peak = Math.Max(peak, closes[i]);
        return closes[firstUp] > 0 && peak >= closes[firstUp] * 1.10m;
    }

    /// <summary>平台突破：近 10 日箱体（高低幅 ≤15%），当日阳线收盘突破箱体上沿。</summary>
    private static bool IsPlatformBreakout(IReadOnlyList<CandleBar> asc, int n)
    {
        if (n < 11) return false;
        var cur = asc[n - 1];
        if (!cur.IsBull) return false;
        decimal boxHigh = decimal.MinValue, boxLow = decimal.MaxValue;
        for (var i = n - 11; i < n - 1; i++)
        {
            boxHigh = Math.Max(boxHigh, asc[i].High);
            boxLow = Math.Min(boxLow, asc[i].Low);
        }
        if (boxLow <= 0) return false;
        var tight = (boxHigh - boxLow) / boxLow <= 0.15m;
        return tight && cur.Close > boxHigh;
    }

    /// <summary>缩量回踩企稳：价在 MA10 附近上方、近 3 日相对前 3 日缩量、当日未大跌（上升中继）。</summary>
    private static bool IsPullbackStabilize(IReadOnlyList<CandleBar> asc, decimal[] ma10, long[] volumes, int n)
    {
        if (n < 6 || ma10[n - 1] <= 0) return false;
        var cur = asc[n - 1];
        if (cur.Close < ma10[n - 1] * 0.98m) return false;
        var recent3 = AvgVol(volumes, n - 3, 3);
        var prev3 = AvgVol(volumes, n - 6, 3);
        if (prev3 <= 0 || recent3 >= prev3) return false;
        var prevClose = asc[n - 2].Close;
        return prevClose > 0 && cur.Close >= prevClose * 0.97m;
    }

    // —— C. 量价配合形态 ——

    /// <summary>放量突破：当日收盘创近 10 日新高，且成交量 ≥1.5 倍前 10 日均量。</summary>
    private static bool IsVolumeBreakout(IReadOnlyList<CandleBar> asc, decimal[] closes, long[] volumes, int n)
    {
        if (n < 11) return false;
        var cur = asc[n - 1];
        decimal priorMax = decimal.MinValue;
        for (var i = n - 11; i < n - 1; i++) priorMax = Math.Max(priorMax, closes[i]);
        var avgVol = AvgVol(volumes, n - 11, 10);
        return cur.Close > priorMax && avgVol > 0 && volumes[n - 1] >= avgVol * 1.5m;
    }

    /// <summary>缩量回调：近 3 日相对前 3 日明显缩量(≤80%)、价格走平/小回，且仍站上 MA20（健康回踩）。</summary>
    private static bool IsVolumeShrinkPullback(decimal[] closes, decimal[] ma20, long[] volumes, int n)
    {
        if (n < 7 || ma20[n - 1] <= 0) return false;
        var recent3 = AvgVol(volumes, n - 3, 3);
        var prev3 = AvgVol(volumes, n - 6, 3);
        if (prev3 <= 0 || recent3 > prev3 * 0.8m) return false;
        if (closes[n - 1] > closes[n - 4]) return false;       // 走平或小回，不是上涨
        return closes[n - 1] >= ma20[n - 1];                   // 仍在中期均线上方
    }

    // —— 工具 ——

    private static decimal AvgVol(long[] volumes, int start, int count)
    {
        if (start < 0 || count <= 0 || start + count > volumes.Length) return 0m;
        long sum = 0;
        for (var i = start; i < start + count; i++) sum += volumes[i];
        return (decimal)sum / count;
    }

    /// <summary>简单移动平均序列：r[i] = closes[i-p+1..i] 均值，不足 p 根记 0。</summary>
    private static decimal[] Sma(decimal[] closes, int p)
    {
        var r = new decimal[closes.Length];
        decimal sum = 0;
        for (var i = 0; i < closes.Length; i++)
        {
            sum += closes[i];
            if (i >= p) sum -= closes[i - p];
            r[i] = i >= p - 1 ? sum / p : 0m;
        }
        return r;
    }
}
