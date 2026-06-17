using AIStock.Core.Models;

namespace AIStock.Selection.Backtest;

// BacktestEntryTiming, BacktestConfig, ExitRule, EntryRule, ExitContext, EntryContext, ExitResult, EntryResult
// 已统一到 AIStock.Core.Models 命名空间。本文件仅保留 Selection 引擎特有的类型。

/// <summary>一个选股信号（某交易日选出的一只股票）。</summary>
public class BacktestSignal
{
    public DateTime Date { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>信号打分（用于组合约束下同日的信号排序，越高越优先）。</summary>
    public decimal Score { get; set; }
}

/// <summary>回测用日 K（按 code 分组传入引擎）。已统一到 Core.Models。</summary>

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
    /// <summary>收益率（%，已扣除交易摩擦）。</summary>
    public decimal ReturnPct { get; set; }
    /// <summary>卖出是否因跌停一字顺延过。</summary>
    public bool ExitDeferred { get; set; }
    /// <summary>出场原因：time（持有到期）/ stoploss（止损）/ takeprofit（止盈）/ trailing_stop / atr_stop / ma{N}_exit。</summary>
    public string ExitReason { get; set; } = "time";
    /// <summary>持有期最高浮盈（%，相对买入价）。</summary>
    public decimal MaxRisePct { get; set; }
    /// <summary>持有期最大浮亏（%，相对买入价，负值）。</summary>
    public decimal MaxDropPct { get; set; }
    public bool Win => ReturnPct > 0;
}

/// <summary>选股回测汇总报告（统一版）。合并原 Selection.Backtest.BacktestReport 与 Core.Models.BacktestResult。</summary>
public class BacktestReport
{
    // === 当前 Selection 报告字段 ===
    public int HoldDays { get; set; }
    public string Entry { get; set; } = string.Empty;
    /// <summary>信号总数（含因数据缺失未成交的）。</summary>
    public int TotalSignals { get; set; }
    /// <summary>实际成交笔数。</summary>
    public int ExecutedTrades { get; set; }
    /// <summary>因 K 线数据不足跳过的信号数。</summary>
    public int SkippedNoData { get; set; }
    /// <summary>因买入日一字涨停买不进剔除的信号数。</summary>
    public int SkippedUntradable { get; set; }
    /// <summary>因破位否决（收盘跌破MA10 / MA5拐头向下斜率过大）放弃的信号数。</summary>
    public int SkippedBreakdown { get; set; }
    /// <summary>卖出日一字跌停顺延的笔数。</summary>
    public int DeferredExits { get; set; }
    /// <summary>因资金/仓位约束跳过的信号数（组合约束模式）。</summary>
    public int SkippedCapital { get; set; }
    /// <summary>本次回测使用的单笔往返摩擦（%）。</summary>
    public decimal FrictionPct { get; set; }

    /// <summary>胜率（%，收益>0 占比）。</summary>
    public decimal WinRatePct { get; set; }
    /// <summary>平均收益率（%）。</summary>
    public decimal AvgReturnPct { get; set; }
    /// <summary>收益中位数（%）。</summary>
    public decimal MedianReturnPct { get; set; }
    /// <summary>盈亏比（平均盈利 / 平均亏损绝对值）；无亏损时为 null。</summary>
    public decimal? ProfitFactor { get; set; }
    /// <summary>收益标准差（%）。</summary>
    public decimal StdDevPct { get; set; }
    /// <summary>累计收益曲线最大回撤（百分点）。</summary>
    public decimal MaxDrawdownPct { get; set; }
    /// <summary>平均持有期最高浮盈（%）。</summary>
    public decimal AvgMaxRisePct { get; set; }
    /// <summary>平均持有期最大浮亏（%）。</summary>
    public decimal AvgMaxDropPct { get; set; }
    public decimal BestReturnPct { get; set; }
    public decimal WorstReturnPct { get; set; }
    public List<SelectionBacktestTrade> Trades { get; set; } = new();

    // === ★ 新增：Strategy 引擎字段 ===
    public decimal InitialCapital { get; set; }
    public decimal FinalCapital { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal AnnualizedReturn { get; set; }
    public decimal SharpeRatio { get; set; }

    // === ★ 新增：基准对比 ===
    public decimal? BenchmarkReturn { get; set; }
    public decimal? Alpha { get; set; }
    public decimal? Beta { get; set; }
    public decimal? InformationRatio { get; set; }

    // === ★ 新增：权益曲线 ===
    public List<Core.Models.DailyNav> EquityCurve { get; set; } = new();

    // === ★ 新增：统计检验 ===
    public decimal? AvgReturnPValue { get; set; }
    public decimal? WinRateCI_Lower { get; set; }
    public decimal? WinRateCI_Upper { get; set; }

    // === ★ 新增：数据覆盖告警 ===
    public List<string> Warnings { get; set; } = new();
    public DateTime? NewsCoverageStart { get; set; }
    public bool MarketCapAvailable { get; set; } = true;
}

// BacktestBar 已统一到 AIStock.Core.Models.BacktestBar，
// 通过上行 "using AIStock.Core.Models;" 自动生效，无需别名。

/// <summary>回测报告持久化辅助。</summary>
public static class BacktestReportPersistence
{
    /// <summary>
    /// 序列化为入库用的精简报告：保留全部标量指标与告警，剔除体量大的逐笔交易（Trades）
    /// 与逐日权益曲线（EquityCurve）——这两项可由相同参数+代码重跑复现，不必落库。
    /// 临时置空后序列化再还原，不影响返回给调用方的报告对象。
    /// </summary>
    public static string ToSummaryJson(this BacktestReport report)
    {
        var trades = report.Trades;
        var curve = report.EquityCurve;
        report.Trades = new();
        report.EquityCurve = new();
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(report);
        }
        finally
        {
            report.Trades = trades;
            report.EquityCurve = curve;
        }
    }
}
