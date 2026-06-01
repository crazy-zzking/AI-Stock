using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 持仓记录实体
/// </summary>
[Table("position")]
public class PositionEntity
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
    /// 股票名称
    /// </summary>
    [Column("name")]
    [StringLength(50)]
    public string? Name { get; set; }

    /// <summary>
    /// 持股数量（股）
    /// </summary>
    [Column("volume")]
    public long Volume { get; set; }

    /// <summary>
    /// 可卖数量（股）
    /// </summary>
    [Column("sellable_volume")]
    public long SellableVolume { get; set; }

    /// <summary>
    /// 成本价（元）
    /// </summary>
    [Column("cost_price")]
    public decimal CostPrice { get; set; }

    /// <summary>
    /// 现价（元）
    /// </summary>
    [Column("current_price")]
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 盈亏金额（元）
    /// </summary>
    [Column("profit")]
    public decimal Profit { get; set; }

    /// <summary>
    /// 盈亏比例（%）
    /// </summary>
    [Column("profit_rate")]
    public decimal ProfitRate { get; set; }

    /// <summary>
    /// 策略名称
    /// </summary>
    [Column("strategy_name")]
    [StringLength(100)]
    public string? StrategyName { get; set; }

    /// <summary>
    /// 买入时间
    /// </summary>
    [Column("buy_time")]
    public DateTime? BuyTime { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
