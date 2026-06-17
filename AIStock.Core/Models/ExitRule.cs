using System.Text.Json.Serialization;

namespace AIStock.Core.Models;

/// <summary>回测用日 K 线数据。</summary>
public class BacktestBar
{
    public DateTime Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    /// <summary>成交量（股），用于量比计算等场景。</summary>
    public long Volume { get; set; }
}

/// <summary>出场规则类型。</summary>
public enum ExitRuleType
{
    /// <summary>固定止损：相对买入价下跌 X% 即卖。</summary>
    FixedStopLoss,

    /// <summary>固定止盈：相对买入价上涨 X% 即卖。</summary>
    FixedTakeProfit,

    /// <summary>移动止损：从持仓期间最高价回落 X% 即卖。</summary>
    TrailingStop,

    /// <summary>移动止盈：浮盈曾达 Param1% 后激活，自最高价回落 Param2% 即卖（锁利）。</summary>
    TrailingTakeProfit,

    /// <summary>ATR 动态止损：入场价 − N × ATR(14) 即卖。</summary>
    AtrStop,

    /// <summary>均线死叉出场：收盘价跌破 MA(X) 即卖。</summary>
    MaCrossDown,

    /// <summary>时间止盈：持有 ≥ N 个交易日则卖出（替代原 HoldDays）。</summary>
    TimeExit,

    /// <summary>放量出场：当日成交量 > 入场前 N 日均量 × M 倍。</summary>
    VolumeSpike,
}

/// <summary>一条出场规则。按 Priority 升序执行，第一条触发即出场。</summary>
public class ExitRule
{
    /// <summary>规则类型。</summary>
    public ExitRuleType Type { get; set; }

    /// <summary>参数1（百分比 / ATR 倍数 / MA 周期 / 天数 / 量比 等）。</summary>
    public decimal Param1 { get; set; }

    /// <summary>可选参数2（例如 VolumeSpike 的倍数）。</summary>
    public decimal? Param2 { get; set; }

    /// <summary>优先级（越小越先判断）。</summary>
    public int Priority { get; set; }

    /// <summary>执行体（纯函数，不序列化到 JSON/DB）。</summary>
    [JsonIgnore]
    public Func<ExitContext, ExitResult?>? Evaluator { get; set; }
}

/// <summary>出场规则判断上下文（逐日更新）。</summary>
public class ExitContext
{
    /// <summary>入场价。</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>持仓期间最高价（逐日更新）。</summary>
    public decimal HighestHigh { get; set; }

    /// <summary>持仓期间最低价（逐日更新）。</summary>
    public decimal LowestLow { get; set; }

    /// <summary>当日 K 线。</summary>
    public BacktestBar Today { get; set; } = null!;

    /// <summary>前一日 K 线（可能为 null，首日）。</summary>
    public BacktestBar? PrevBar { get; set; }

    /// <summary>入场时的 ATR(14) 值。</summary>
    public decimal Atr14 { get; set; }

    /// <summary>当日 MA 值（根据规则 Param1 周期的 MA）。</summary>
    public decimal MaValue { get; set; }

    /// <summary>入场前 N 日均量。</summary>
    public decimal AvgVolume { get; set; }

    /// <summary>已持交易日数（不含入场日）。</summary>
    public int DaysHeld { get; set; }

    /// <summary>当日涨停幅度（用于一字板判断）。</summary>
    public decimal LimitRatio { get; set; }
}

/// <summary>出场结果。</summary>
public class ExitResult
{
    /// <summary>出场价。</summary>
    public decimal ExitPrice { get; set; }

    /// <summary>出场原因标识（如 "trailing_stop", "atr_stop", "ma20_exit"）。</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>入场规则类型。</summary>
public enum EntryRuleType
{
    /// <summary>T+1 开盘买入（原 NextOpen）。</summary>
    NextOpen,

    /// <summary>信号日收盘买入（原 SignalClose）。</summary>
    SignalClose,

    /// <summary>限价回调：信号后 MaxWaitDays 日内，若回调到 Param1% 则买入。</summary>
    LimitPullback,

    /// <summary>量能确认：T+1 成交量 > 信号日成交量 × Param1 倍才确认买入。</summary>
    VolumeConfirm,

    /// <summary>跳空限制：T+1 开盘 > 信号日收盘 × (1+Param1%) 则放弃买入。</summary>
    GapLimit,
}

/// <summary>一条入场规则。</summary>
public class EntryRule
{
    /// <summary>规则类型。</summary>
    public EntryRuleType Type { get; set; }

    /// <summary>参数1。</summary>
    public decimal Param1 { get; set; }

    /// <summary>最大等待天数（限价单等场景）。</summary>
    public int MaxWaitDays { get; set; } = 3;

    /// <summary>执行体（纯函数，不序列化到 JSON/DB）。</summary>
    [JsonIgnore]
    public Func<EntryContext, EntryResult?>? Evaluator { get; set; }
}

/// <summary>入场规则判断上下文。</summary>
public class EntryContext
{
    /// <summary>信号日索引（在 ordered bars 中的位置）。</summary>
    public int SigIdx { get; set; }

    /// <summary>信号日收盘价。</summary>
    public decimal SignalClose { get; set; }

    /// <summary>已排序的 K 线列表。</summary>
    public List<BacktestBar> OrderedBars { get; set; } = null!;

    /// <summary>涨停幅度。</summary>
    public decimal LimitRatio { get; set; }
}

/// <summary>入场结果。</summary>
public class EntryResult
{
    /// <summary>入场索引（在 ordered bars 中的位置）。</summary>
    public int EntryIdx { get; set; }

    /// <summary>入场价。</summary>
    public decimal EntryPrice { get; set; }
}
