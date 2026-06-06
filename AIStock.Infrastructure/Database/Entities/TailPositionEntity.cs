using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 尾盘策略持仓跟踪。尾盘买入时落一条 Open 记录（含成本/止损/目标持有日），
/// 卖出服务据此判断"持有到期/止损"并平仓——使 DryRun 下也能完整模拟"买入→持有→卖出"闭环
/// （券商账户持仓只有 Live 才有，且无买入日/策略，无法支撑持有期判断）。
/// </summary>
[Table("tail_position")]
public class TailPositionEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Column("name")]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [Column("strategy")]
    [StringLength(50)]
    public string Strategy { get; set; } = string.Empty;

    /// <summary>买入交易日</summary>
    [Column("buy_date")]
    public DateTime BuyDate { get; set; }

    [Column("buy_price")]
    public decimal BuyPrice { get; set; }

    [Column("volume")]
    public long Volume { get; set; }

    /// <summary>止损价（现价跌破即止损卖出）</summary>
    [Column("stop_loss_price")]
    public decimal StopLossPrice { get; set; }

    /// <summary>目标持有交易日数（满则到期卖出）</summary>
    [Column("hold_days")]
    public int HoldDays { get; set; }

    [Column("signal_id")]
    [StringLength(100)]
    public string SignalId { get; set; } = string.Empty;

    /// <summary>是否 DryRun 下的模拟持仓</summary>
    [Column("is_dry_run")]
    public bool IsDryRun { get; set; }

    /// <summary>0=Open 持有中，1=Closed 已平仓</summary>
    [Column("status")]
    public int Status { get; set; }

    [Column("sell_date")]
    public DateTime? SellDate { get; set; }

    [Column("sell_price")]
    public decimal? SellPrice { get; set; }

    /// <summary>平仓原因：hold-expired / stop-loss</summary>
    [Column("sell_reason")]
    [StringLength(50)]
    public string? SellReason { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
