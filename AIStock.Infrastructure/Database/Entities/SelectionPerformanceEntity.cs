using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 选股信号前向绩效 — 一行 = (交易日, 策略, 代码) 一个正式信号（取该日该策略 run_at 最新一批）。
/// 由 Worker 每日收盘后物化并随 K 线到位逐日补算 T+1/T+3/T+5 收益与沪深300超额，
/// 与回放回测的区别：这是"当时真实选出的票"的不可篡改成绩单，不随代码/参数改动漂移。
/// </summary>
[Table("selection_performance")]
public class SelectionPerformanceEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>信号交易日（选股所基于的快照交易日）</summary>
    [Column("trading_date")]
    public DateTime TradingDate { get; set; }

    /// <summary>策略键（lowdip/trend/theme/ambush/import...）</summary>
    [Column("strategy")]
    [StringLength(50)]
    public string Strategy { get; set; } = string.Empty;

    /// <summary>策略显示名（写入时固化）</summary>
    [Column("strategy_name")]
    [StringLength(100)]
    public string StrategyName { get; set; } = string.Empty;

    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Column("name")]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>来源选股批次（selection_result.id）</summary>
    [Column("selection_result_id")]
    public long SelectionResultId { get; set; }

    /// <summary>选股时综合得分</summary>
    [Column("score")]
    public decimal Score { get; set; }

    /// <summary>信号日收盘价（元）</summary>
    [Column("signal_close")]
    public decimal SignalClose { get; set; }

    /// <summary>入场日（信号次日首个交易日；null = K线未到位）</summary>
    [Column("entry_date")]
    public DateTime? EntryDate { get; set; }

    /// <summary>入场价 = 入场日开盘价（元）</summary>
    [Column("entry_price")]
    public decimal? EntryPrice { get; set; }

    /// <summary>不可成交：入场日开盘即≈涨停价（一字板买不进），收益不计入统计</summary>
    [Column("untradable")]
    public bool Untradable { get; set; }

    /// <summary>T+1 收盘相对入场价收益（%）</summary>
    [Column("ret1")]
    public decimal? Ret1 { get; set; }

    /// <summary>T+3 收盘相对入场价收益（%）</summary>
    [Column("ret3")]
    public decimal? Ret3 { get; set; }

    /// <summary>T+5 收盘相对入场价收益（%）</summary>
    [Column("ret5")]
    public decimal? Ret5 { get; set; }

    /// <summary>T+1 相对沪深300同窗口超额（百分点）</summary>
    [Column("excess1")]
    public decimal? Excess1 { get; set; }

    /// <summary>T+3 相对沪深300同窗口超额（百分点）</summary>
    [Column("excess3")]
    public decimal? Excess3 { get; set; }

    /// <summary>T+5 相对沪深300同窗口超额（百分点）</summary>
    [Column("excess5")]
    public decimal? Excess5 { get; set; }

    /// <summary>pending（待K线）/ partial（部分窗口已算）/ final（T+5 已齐或确认不可成交）</summary>
    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = PerformanceStatus.Pending;

    /// <summary>信号时大盘环境（weak/neutral/strong/unknown，取自选股结果的大盘描述）——支撑"策略×环境"分桶。</summary>
    [Column("market_regime")]
    [StringLength(20)]
    public string MarketRegime { get; set; } = "unknown";

    /// <summary>信号时生效的配置版本（自来源批次固化）——策略迭代前后成绩分段对照。</summary>
    [Column("config_version")]
    [StringLength(50)]
    public string ConfigVersion { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>selection_performance.status 取值</summary>
public static class PerformanceStatus
{
    public const string Pending = "pending";
    public const string Partial = "partial";
    public const string Final = "final";
}
