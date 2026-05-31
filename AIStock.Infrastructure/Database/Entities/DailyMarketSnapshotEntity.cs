using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 每日市场快照实体 — 收盘后聚合 行情/估值/资金流/技术指标，作为选股引擎的主数据源。
/// 唯一键 (code, date)。
/// </summary>
[Table("daily_market_snapshot")]
public class DailyMarketSnapshotEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>股票代码</summary>
    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>股票名称</summary>
    [Column("name")]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>交易日期</summary>
    [Column("date")]
    public DateTime Date { get; set; }

    // —— 行情 ——
    /// <summary>收盘价</summary>
    [Column("close")]
    public decimal Close { get; set; }

    /// <summary>当日涨跌幅（%）</summary>
    [Column("change_percent")]
    public decimal ChangePercent { get; set; }

    /// <summary>换手率（%）</summary>
    [Column("turnover_rate")]
    public decimal TurnoverRate { get; set; }

    /// <summary>量比</summary>
    [Column("volume_ratio")]
    public decimal VolumeRatio { get; set; }

    /// <summary>振幅（%）= (最高-最低)/昨收</summary>
    [Column("amplitude")]
    public decimal Amplitude { get; set; }

    /// <summary>是否涨停</summary>
    [Column("is_limit_up")]
    public bool IsLimitUp { get; set; }

    // —— 估值 ——
    /// <summary>总市值（元）</summary>
    [Column("total_market_cap")]
    public decimal TotalMarketCap { get; set; }

    /// <summary>市盈率（TTM）</summary>
    [Column("pe_ttm")]
    public decimal PeTtm { get; set; }

    // —— 资金流 ——
    /// <summary>主力净流入（元）</summary>
    [Column("main_net_inflow")]
    public decimal MainNetInflow { get; set; }

    // —— 位置 ——
    /// <summary>近 20 日涨幅（%）</summary>
    [Column("rise20d")]
    public decimal Rise20d { get; set; }

    // —— 技术指标 ——
    [Column("ma5")] public decimal Ma5 { get; set; }
    [Column("ma10")] public decimal Ma10 { get; set; }
    [Column("ma20")] public decimal Ma20 { get; set; }
    [Column("macd_dif")] public decimal MacdDif { get; set; }
    [Column("macd_dea")] public decimal MacdDea { get; set; }
    /// <summary>是否 MACD 金叉初期（DIF 上穿 DEA 且刚由负转正附近）</summary>
    [Column("macd_golden_cross")] public bool MacdGoldenCross { get; set; }
    [Column("rsi")] public decimal Rsi { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
