using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 每日资金流（轻量历史表）。东财 fflow/daykline 历史落库，供回放回测补资金面
/// （kline_data 没有资金流）。唯一键 (code, date)。
/// </summary>
[Table("daily_capital_flow")]
public class CapitalFlowEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Column("date")]
    public DateTime Date { get; set; }

    /// <summary>主力净流入（元）= 大单 + 超大单</summary>
    [Column("main_net_inflow")]
    public decimal MainNetInflow { get; set; }

    [Column("super_large_net")] public decimal SuperLargeNetInflow { get; set; }
    [Column("large_net")] public decimal LargeNetInflow { get; set; }
    [Column("medium_net")] public decimal MediumNetInflow { get; set; }
    [Column("small_net")] public decimal SmallNetInflow { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
