using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 龙虎榜席位明细（每个买/卖席位一行），支持游资/营业部追踪与席位级查询。
/// 每次采集对 (code, date) 先删后插，避免累积。
/// </summary>
[Table("dragon_tiger_seat")]
public class DragonTigerSeatEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>交易日期</summary>
    [Column("date")]
    public DateTime Date { get; set; }

    /// <summary>股票代码</summary>
    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>股票名称</summary>
    [Column("name")]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>席位名称（营业部 / "机构专用" / "沪股通专用" 等）</summary>
    [Column("seat_name")]
    [StringLength(200)]
    public string SeatName { get; set; } = string.Empty;

    /// <summary>买卖方向：buy / sell</summary>
    [Column("side")]
    [StringLength(8)]
    public string Side { get; set; } = string.Empty;

    /// <summary>该席位买入额（元）</summary>
    [Column("buy_amount")]
    public decimal BuyAmount { get; set; }

    /// <summary>该席位卖出额（元）</summary>
    [Column("sell_amount")]
    public decimal SellAmount { get; set; }

    /// <summary>该席位净额（元）= 买 - 卖</summary>
    [Column("net_amount")]
    public decimal NetAmount { get; set; }

    /// <summary>是否机构专用席位</summary>
    [Column("is_institution")]
    public bool IsInstitution { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
