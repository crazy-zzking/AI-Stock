using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 交易候选池：每日选股经 LLM 复评后「建议买入(buy)」的票自动入池，
/// 携带 AI 推荐理由 + 买入价/止损/止盈，供人工在交易页一键手动下单。
/// 同 (交易日 + 策略 + 代码) 唯一，复评重跑时 upsert（不重复入池）。
/// </summary>
[Table("trade_candidate")]
public class TradeCandidateEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>选股交易日</summary>
    [Column("trading_date")]
    public DateTime TradingDate { get; set; }

    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Column("name")]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>来源策略键</summary>
    [Column("strategy")]
    [StringLength(50)]
    public string Strategy { get; set; } = string.Empty;

    /// <summary>来源策略显示名</summary>
    [Column("strategy_name")]
    [StringLength(80)]
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>来源选股批次 selection_result.id</summary>
    [Column("source_batch_id")]
    public long SourceBatchId { get; set; }

    /// <summary>LLM 建议等级（入池恒为 buy，预留）</summary>
    [Column("recommendation")]
    [StringLength(20)]
    public string Recommendation { get; set; } = "buy";

    /// <summary>置信度 0-100</summary>
    [Column("confidence")]
    public int Confidence { get; set; }

    /// <summary>风险/排雷标签（JSON 数组字符串）</summary>
    [Column("risk_flags")]
    public string RiskFlags { get; set; } = "[]";

    /// <summary>AI 推荐理由（核心逻辑/看点）</summary>
    [Column("narrative")]
    [StringLength(1000)]
    public string Narrative { get; set; } = string.Empty;

    /// <summary>选股日参考收盘价</summary>
    [Column("ref_close")]
    public decimal RefClose { get; set; }

    [Column("buy_low")]
    public decimal BuyLow { get; set; }

    [Column("buy_high")]
    public decimal BuyHigh { get; set; }

    [Column("stop_loss")]
    public decimal StopLoss { get; set; }

    [Column("take_profit")]
    public decimal TakeProfit { get; set; }

    /// <summary>价位依据说明</summary>
    [Column("plan_basis")]
    [StringLength(500)]
    public string PlanBasis { get; set; } = string.Empty;

    /// <summary>0=待处理 1=已下单 2=已忽略</summary>
    [Column("status")]
    public int Status { get; set; }

    /// <summary>下单后回写的订单号</summary>
    [Column("order_id")]
    [StringLength(100)]
    public string? OrderId { get; set; }

    /// <summary>实际下单价</summary>
    [Column("order_price")]
    public decimal? OrderPrice { get; set; }

    /// <summary>实际下单量（股）</summary>
    [Column("order_volume")]
    public long? OrderVolume { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
