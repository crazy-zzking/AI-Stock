namespace AIStock.Selection.Backtest;

/// <summary>买入时点。</summary>
public enum BacktestEntryTiming
{
    /// <summary>信号次日(T+1)开盘买入（贴近实盘，默认）。</summary>
    NextOpen,
    /// <summary>信号当日收盘买入（理想化，用于对比）。</summary>
    SignalClose,
}

/// <summary>选股回测参数。</summary>
public class BacktestConfig
{
    /// <summary>持有交易日数（持有到第 N 个交易日收盘卖出）。</summary>
    public int HoldDays { get; set; } = 5;
    /// <summary>买入时点。</summary>
    public BacktestEntryTiming Entry { get; set; } = BacktestEntryTiming.NextOpen;
}

/// <summary>一个选股信号（某交易日选出的一只股票）。</summary>
public class BacktestSignal
{
    public DateTime Date { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>回测用日 K（按 code 分组传入引擎）。</summary>
public class BacktestBar
{
    public DateTime Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}

/// <summary>单笔回测成交结果。</summary>
public class SelectionBacktestTrade
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime SignalDate { get; set; }
    public DateTime EntryDate { get; set; }
    public decimal EntryPrice { get; set; }
    public DateTime ExitDate { get; set; }
    public decimal ExitPrice { get; set; }
    public int HoldDays { get; set; }
    /// <summary>收益率（%）。</summary>
    public decimal ReturnPct { get; set; }
    /// <summary>持有期最高浮盈（%，相对买入价）。</summary>
    public decimal MaxRisePct { get; set; }
    /// <summary>持有期最大浮亏（%，相对买入价，负值）。</summary>
    public decimal MaxDropPct { get; set; }
    public bool Win => ReturnPct > 0;
}

/// <summary>选股回测汇总报告。</summary>
public class BacktestReport
{
    public int HoldDays { get; set; }
    public string Entry { get; set; } = string.Empty;

    /// <summary>信号总数（含因数据缺失未成交的）。</summary>
    public int TotalSignals { get; set; }
    /// <summary>实际成交笔数。</summary>
    public int ExecutedTrades { get; set; }
    /// <summary>因 K 线数据不足跳过的信号数。</summary>
    public int SkippedNoData { get; set; }

    /// <summary>胜率（%，收益&gt;0 占比）。</summary>
    public decimal WinRatePct { get; set; }
    /// <summary>平均收益率（%）。</summary>
    public decimal AvgReturnPct { get; set; }
    /// <summary>收益中位数（%）。</summary>
    public decimal MedianReturnPct { get; set; }
    /// <summary>盈亏比（平均盈利 / 平均亏损绝对值）；无亏损时为 null。</summary>
    public decimal? ProfitFactor { get; set; }
    /// <summary>收益标准差（%）。</summary>
    public decimal StdDevPct { get; set; }
    /// <summary>按时间累计收益曲线的最大回撤（百分点）。</summary>
    public decimal MaxDrawdownPct { get; set; }
    /// <summary>平均持有期最高浮盈（%）。</summary>
    public decimal AvgMaxRisePct { get; set; }
    /// <summary>平均持有期最大浮亏（%）。</summary>
    public decimal AvgMaxDropPct { get; set; }
    public decimal BestReturnPct { get; set; }
    public decimal WorstReturnPct { get; set; }

    public List<SelectionBacktestTrade> Trades { get; set; } = new();
}
