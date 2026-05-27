using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 交易记录实体
/// </summary>
[Table("trade_record")]
public class TradeRecordEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 订单ID
    /// </summary>
    [Column("order_id")]
    public long? OrderId { get; set; }

    /// <summary>
    /// 委托编号
    /// </summary>
    [Column("agree_id")]
    public long? AgreeId { get; set; }

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
    /// 交易方向（buy/sell）
    /// </summary>
    [Column("direction")]
    [StringLength(10)]
    public string Direction { get; set; } = string.Empty;

    /// <summary>
    /// 价格（元）
    /// </summary>
    [Column("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// 数量（股）
    /// </summary>
    [Column("volume")]
    public long Volume { get; set; }

    /// <summary>
    /// 金额（元）
    /// </summary>
    [Column("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// 手续费（元）
    /// </summary>
    [Column("commission")]
    public decimal Commission { get; set; }

    /// <summary>
    /// 策略名称
    /// </summary>
    [Column("strategy_name")]
    [StringLength(100)]
    public string? StrategyName { get; set; }

    /// <summary>
    /// 信号ID
    /// </summary>
    [Column("signal_id")]
    [StringLength(100)]
    public string? SignalId { get; set; }

    /// <summary>
    /// 状态（pending/accepted/completed/failed/cancelled）
    /// </summary>
    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 状态消息
    /// </summary>
    [Column("status_message")]
    [StringLength(500)]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// 交易时间
    /// </summary>
    [Column("trade_time")]
    public DateTime? TradeTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
