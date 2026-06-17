using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 回测结果持久化 — 每次回测保存一条记录，含参数与结果 JSON，支持历史回溯与对比。
/// </summary>
[Table("backtest_result")]
public class BacktestResultEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>回测运行时间（UTC）</summary>
    [Column("run_at")]
    public DateTime RunAt { get; set; } = DateTime.UtcNow;

    /// <summary>回测类型：selection（选股回测）/ replay（回放回测）/ strategy（策略回测）</summary>
    [Column("backtest_type")]
    [StringLength(20)]
    public string BacktestType { get; set; } = string.Empty;

    /// <summary>策略键（replay/strategy 回测用）</summary>
    [Column("strategy_key")]
    [StringLength(50)]
    public string? StrategyKey { get; set; }

    /// <summary>策略显示名（写入时固化）</summary>
    [Column("strategy_name")]
    [StringLength(100)]
    public string? StrategyName { get; set; }

    /// <summary>回测参数 JSON（BacktestConfig 序列化）</summary>
    [Column("config_json", TypeName = "longtext")]
    public string ConfigJson { get; set; } = "{}";

    /// <summary>回测报告 JSON（BacktestReport 序列化，含 equityCurve/trades/warnings）</summary>
    [Column("report_json", TypeName = "longtext")]
    public string ReportJson { get; set; } = "{}";

    /// <summary>样本量（成交笔数）</summary>
    [Column("executed_trades")]
    public int ExecutedTrades { get; set; }

    /// <summary>胜率（%）</summary>
    [Column("win_rate_pct")]
    public decimal WinRatePct { get; set; }

    /// <summary>平均收益（%）</summary>
    [Column("avg_return_pct")]
    public decimal AvgReturnPct { get; set; }

    /// <summary>Sharpe 比率（有则存）</summary>
    [Column("sharpe_ratio")]
    public decimal? SharpeRatio { get; set; }

    /// <summary>最大回撤（pt）</summary>
    [Column("max_drawdown_pct")]
    public decimal MaxDrawdownPct { get; set; }

    /// <summary>Alpha（%）</summary>
    [Column("alpha_pct")]
    public decimal? AlphaPct { get; set; }

    /// <summary>Beta</summary>
    [Column("beta")]
    public decimal? Beta { get; set; }

    /// <summary>创建时间（本地）</summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
