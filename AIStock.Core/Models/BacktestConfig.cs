using System.Text.Json.Serialization;

namespace AIStock.Core.Models;

/// <summary>
/// 统一回测配置（合并 Selection 引擎 + Strategy 引擎两套 BacktestConfig）。
/// 字段来源标注 [S]=Selection 引擎原字段, [T]=Strategy 引擎原字段, [新]=本次新增。
/// </summary>
public class BacktestConfig
{
    // ========== Selection 引擎字段 [S] ==========

    /// <summary>持有交易日数（持有到第 N 个交易日收盘卖出）。[S]</summary>
    public int HoldDays { get; set; } = 5;

    /// <summary>买入时点。[S]</summary>
    public BacktestEntryTiming Entry { get; set; } = BacktestEntryTiming.NextOpen;

    /// <summary>可成交性约束（一字涨停买不进剔除、一字跌停卖出顺延）。[S]</summary>
    public bool ApplyTradability { get; set; } = true;

    /// <summary>单笔往返交易摩擦（%，佣金+印花税+滑点合计）。[S]</summary>
    public decimal FrictionPct { get; set; } = 0.3m;

    /// <summary>止损线（%，相对买入价）。保留向后兼容，优先使用 ExitRules。[S]</summary>
    public decimal StopLossPct { get; set; }

    /// <summary>止盈线（%，相对买入价）。保留向后兼容，优先使用 ExitRules。[S]</summary>
    public decimal TakeProfitPct { get; set; }

    // ========== Strategy 引擎字段 [T] ==========

    /// <summary>初始资金。[T]</summary>
    public decimal InitialCapital { get; set; } = 1_000_000m;

    /// <summary>手续费率（%）。[T]</summary>
    public decimal CommissionRate { get; set; } = 0.03m;

    /// <summary>印花税率（%）。[T]</summary>
    public decimal StampTaxRate { get; set; } = 0.1m;

    /// <summary>滑点（%）。[T]</summary>
    public decimal Slippage { get; set; } = 0.1m;

    /// <summary>冲击成本（%）。[T]</summary>
    public decimal ImpactCost { get; set; } = 0.05m;

    /// <summary>单笔最大仓位比例（%）。[T]</summary>
    public decimal MaxPositionPercent { get; set; } = 10m;

    /// <summary>最大持仓数量。[T]</summary>
    public int MaxPositions { get; set; } = 10;

    /// <summary>涨停板无法买入。[T]</summary>
    public bool EnforceLimitUp { get; set; } = true;

    /// <summary>跌停板无法卖出。[T]</summary>
    public bool EnforceLimitDown { get; set; } = true;

    // ========== 新增：进出场规则 [新] ==========

    /// <summary>出场规则列表（按 Priority 排序，第一条触发即执行）。
    /// 非空时优先使用；为空但 StopLossPct/TakeProfitPct > 0 时自动从旧字段构造（向后兼容）。[新]</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExitRule>? ExitRules { get; set; }

    /// <summary>入场规则列表。非空时覆盖 Entry 字段的简单逻辑。[新]</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EntryRule>? EntryRules { get; set; }

    // ========== 新增：组合约束 [新] ==========

    /// <summary>是否启用组合层面资金约束（同一日多信号按打分分配资金/持仓上限）。[新]</summary>
    public bool UsePortfolioConstraints { get; set; }

    /// <summary>组合最大总资金（组合约束模式下使用，null=InitialCapital）。[新]</summary>
    public decimal? MaxCapital { get; set; }

    // ========== 新增：出场预设快捷方式 [新] ==========

    /// <summary>出场预设名称（default / trend_follow / swing / grid / atr_swing），设了则自动填充 ExitRules。[新]</summary>
    public string? ExitPreset { get; set; }

    // ========== 新增：入场破位否决 [新] ==========

    /// <summary>破位则不买入：信号日收盘跌破 MA10，或 MA5 较前一日下跌超过 <see cref="Ma5DownSlopeMaxPct"/>%（任一成立）即放弃该信号。
    /// 在信号日(T)收盘 + 截至 T 的均线上判定，无前视。默认关闭以保持历史回测口径。[新]</summary>
    public bool RejectBreakdown { get; set; }

    /// <summary>MA5 拐头向下「斜率太大」阈值（%）：(MA5昨−MA5今)/MA5昨×100 超过该值即视为破位。默认 1。[新]</summary>
    public decimal Ma5DownSlopeMaxPct { get; set; } = 1m;
}

/// <summary>买入时点枚举（统一放到 Core.Models，避免 Selection 命名空间依赖）。</summary>
public enum BacktestEntryTiming
{
    /// <summary>信号次日(T+1)开盘买入（贴近实盘，默认）。</summary>
    NextOpen,
    /// <summary>信号当日收盘买入（理想化，用于对比）。</summary>
    SignalClose,
}
