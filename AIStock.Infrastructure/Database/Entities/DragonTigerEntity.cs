using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 龙虎榜记录实体（个股某日上榜汇总；席位明细以 JSON 存储备查）
/// </summary>
[Table("dragon_tiger")]
public class DragonTigerEntity
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

    /// <summary>上榜原因</summary>
    [Column("reason")]
    [StringLength(200)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>龙虎榜净买入额（元）</summary>
    [Column("net_buy_amount")]
    public decimal NetBuyAmount { get; set; }

    /// <summary>买入合计（元）</summary>
    [Column("buy_amount")]
    public decimal BuyAmount { get; set; }

    /// <summary>卖出合计（元）</summary>
    [Column("sell_amount")]
    public decimal SellAmount { get; set; }

    /// <summary>买方是否含机构专用席位</summary>
    [Column("has_institution")]
    public bool HasInstitution { get; set; }

    /// <summary>买方席位明细（JSON）</summary>
    [Column("buy_seats_json", TypeName = "text")]
    public string? BuySeatsJson { get; set; }

    /// <summary>卖方席位明细（JSON）</summary>
    [Column("sell_seats_json", TypeName = "text")]
    public string? SellSeatsJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
