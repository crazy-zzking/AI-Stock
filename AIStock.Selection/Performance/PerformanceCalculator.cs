namespace AIStock.Selection.Performance;

/// <summary>绩效计算用日K（升序传入）。</summary>
public readonly record struct PerfBar(DateTime Date, decimal Open, decimal Close);

/// <summary>单信号前向绩效计算结果。</summary>
public class ForwardPerformance
{
    public DateTime? EntryDate { get; set; }
    public decimal? EntryPrice { get; set; }

    /// <summary>入场日开盘即≈涨停价（一字板），按买不进处理。</summary>
    public bool Untradable { get; set; }

    /// <summary>T+5 已齐或确认不可成交，不再需要更新。</summary>
    public bool Final { get; set; }

    public decimal? Ret1 { get; set; }
    public decimal? Ret3 { get; set; }
    public decimal? Ret5 { get; set; }
    public decimal? Excess1 { get; set; }
    public decimal? Excess3 { get; set; }
    public decimal? Excess5 { get; set; }
}

/// <summary>
/// 选股信号前向绩效计算（纯函数）。模型：信号次日(T+1)开盘买入，T+1/T+3/T+5 收盘计收益；
/// T+1 开盘≈涨停价视为一字板买不进（untradable，不计收益）；
/// 超额 = 个股收益 − 沪深300同窗口收益（基准同样按 T+1 开盘入场对齐）。
/// T+N 按个股自身K线序列数（停牌日自然顺延），基准按对应自然日期对齐。
/// </summary>
public static class PerformanceCalculator
{
    /// <summary>开盘价达到涨停价 99.8% 即视为一字板（容忍数据源精度误差）。</summary>
    private const decimal LimitOpenTolerance = 0.998m;

    /// <summary>
    /// 按代码与名称推断涨停幅度：创业板(30)/科创板(68) 20%，北交所(4/8/92开头) 30%，ST 5%，其余主板 10%。
    /// </summary>
    public static decimal LimitUpRatio(string code, string name)
    {
        if (name.Contains("ST", StringComparison.OrdinalIgnoreCase)) return 0.05m;
        if (code.StartsWith("30") || code.StartsWith("68")) return 0.20m;
        if (code.StartsWith("4") || code.StartsWith("8") || code.StartsWith("92")) return 0.30m;
        return 0.10m;
    }

    /// <summary>
    /// 计算一个信号的前向绩效。
    /// </summary>
    /// <param name="signalDate">信号交易日。</param>
    /// <param name="signalClose">信号日收盘价（K线缺该日时作 T+1 涨停判定的昨收兜底）。</param>
    /// <param name="code">股票代码（推断涨停幅度）。</param>
    /// <param name="name">股票名称（识别 ST）。</param>
    /// <param name="stockBarsAsc">个股日K（升序，可含信号日及之前的K线，内部自行截取）。</param>
    /// <param name="indexBarsByDate">基准指数日K按日期索引（沪深300）；缺日期时对应超额为 null。</param>
    public static ForwardPerformance Compute(
        DateTime signalDate, decimal signalClose, string code, string name,
        IReadOnlyList<PerfBar> stockBarsAsc,
        IReadOnlyDictionary<DateTime, PerfBar> indexBarsByDate)
    {
        var result = new ForwardPerformance();

        // 信号日之后的K线（T+1 起）
        var fwd = new List<PerfBar>();
        decimal prevClose = signalClose;
        foreach (var bar in stockBarsAsc)
        {
            if (bar.Date.Date <= signalDate.Date)
            {
                prevClose = bar.Close; // 以 K 线为准刷新"昨收"（含信号日自身）
                continue;
            }
            fwd.Add(bar);
        }

        if (fwd.Count == 0) return result; // K线未到位，保持 pending

        var entry = fwd[0];
        result.EntryDate = entry.Date;

        // 一字板判定：T+1 开盘价 ≥ 涨停价 × 容忍系数 → 买不进
        if (prevClose > 0)
        {
            var limitPrice = Math.Round(prevClose * (1 + LimitUpRatio(code, name)), 2);
            if (entry.Open >= limitPrice * LimitOpenTolerance)
            {
                result.Untradable = true;
                result.Final = true;
                return result;
            }
        }

        var entryPrice = entry.Open > 0 ? entry.Open : entry.Close;
        if (entryPrice <= 0) return result; // 脏数据，保持 pending 等修复
        result.EntryPrice = entryPrice;

        // 基准入场：T+1 同日指数开盘价
        decimal? benchEntry = indexBarsByDate.TryGetValue(entry.Date.Date, out var ixEntry) && ixEntry.Open > 0
            ? ixEntry.Open : null;

        (result.Ret1, result.Excess1) = RetAt(fwd, 1, entryPrice, benchEntry, indexBarsByDate);
        (result.Ret3, result.Excess3) = RetAt(fwd, 3, entryPrice, benchEntry, indexBarsByDate);
        (result.Ret5, result.Excess5) = RetAt(fwd, 5, entryPrice, benchEntry, indexBarsByDate);

        result.Final = fwd.Count >= 5;
        return result;
    }

    /// <summary>第 k 个前向交易日收盘的收益率（%）与对沪深300超额（百分点）；K线不足返回 null。</summary>
    private static (decimal? Ret, decimal? Excess) RetAt(
        IReadOnlyList<PerfBar> fwd, int k, decimal entryPrice, decimal? benchEntry,
        IReadOnlyDictionary<DateTime, PerfBar> indexBarsByDate)
    {
        if (fwd.Count < k) return (null, null);
        var bar = fwd[k - 1];
        var ret = Math.Round((bar.Close / entryPrice - 1) * 100, 2);

        decimal? excess = null;
        if (benchEntry is > 0 && indexBarsByDate.TryGetValue(bar.Date.Date, out var ix) && ix.Close > 0)
        {
            var benchRet = (ix.Close / benchEntry.Value - 1) * 100;
            excess = Math.Round(ret - benchRet, 2);
        }
        return (ret, excess);
    }
}
