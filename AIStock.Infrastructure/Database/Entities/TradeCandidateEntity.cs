using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 交易候选池：每日尾盘选股后，把当日各策略选出的票按代码去重（保留最高分，记录被哪些策略命中）
/// 落入本表，携带规则化推荐理由(核心逻辑) + 规则算出的买入价/止损/止盈，供人工在交易页一键手动下单。
/// 不依赖 LLM。同 (交易日 + 代码) 唯一，重跑时 upsert（不重复入池）。
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

    /// <summary>综合评分（取命中策略中的最高分）</summary>
    [Column("score")]
    public decimal Score { get; set; }

    /// <summary>星级评级（0-5）</summary>
    [Column("rating_stars")]
    public int RatingStars { get; set; }

    /// <summary>命中题材标签（JSON 数组字符串）</summary>
    [Column("tags")]
    public string Tags { get; set; } = "[]";

    /// <summary>最高分来源策略键</summary>
    [Column("top_strategy")]
    [StringLength(50)]
    public string TopStrategy { get; set; } = string.Empty;

    /// <summary>最高分来源策略显示名</summary>
    [Column("top_strategy_name")]
    [StringLength(80)]
    public string TopStrategyName { get; set; } = string.Empty;

    /// <summary>命中的所有策略显示名（JSON 数组字符串；多策略命中＝信号更强）</summary>
    [Column("hit_strategies")]
    public string HitStrategies { get; set; } = "[]";

    /// <summary>命中策略数</summary>
    [Column("hit_count")]
    public int HitCount { get; set; }

    /// <summary>来源选股批次 selection_result.id（最高分那条）</summary>
    [Column("source_batch_id")]
    public long SourceBatchId { get; set; }

    /// <summary>推荐理由（规则核心逻辑）</summary>
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
