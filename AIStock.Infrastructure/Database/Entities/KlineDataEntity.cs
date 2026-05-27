using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// K线数据实体
/// </summary>
[Table("kline_data")]
public class KlineDataEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 股票代码
    /// </summary>
    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 日期时间
    /// </summary>
    [Column("datetime")]
    public DateTime DateTime { get; set; }

    /// <summary>
    /// 周期（daily/weekly/monthly/1m/5m/15m/30m/60m）
    /// </summary>
    [Column("interval")]
    [StringLength(20)]
    public string Interval { get; set; } = string.Empty;

    /// <summary>
    /// 开盘价（元）
    /// </summary>
    [Column("open")]
    public decimal Open { get; set; }

    /// <summary>
    /// 收盘价（元）
    /// </summary>
    [Column("close")]
    public decimal Close { get; set; }

    /// <summary>
    /// 最高价（元）
    /// </summary>
    [Column("high")]
    public decimal High { get; set; }

    /// <summary>
    /// 最低价（元）
    /// </summary>
    [Column("low")]
    public decimal Low { get; set; }

    /// <summary>
    /// 成交量（股）
    /// </summary>
    [Column("volume")]
    public long Volume { get; set; }

    /// <summary>
    /// 成交额（元）
    /// </summary>
    [Column("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// 换手率（%）
    /// </summary>
    [Column("turnover_rate")]
    public decimal? TurnoverRate { get; set; }

    /// <summary>
    /// 涨跌幅（%）
    /// </summary>
    [Column("change_percent")]
    public decimal? ChangePercent { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    [Column("source")]
    [StringLength(50)]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
