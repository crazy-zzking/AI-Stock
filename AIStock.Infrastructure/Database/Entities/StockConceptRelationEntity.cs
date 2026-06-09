using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 股票-概念关联实体（题材/概念归属）
/// </summary>
[Table("stock_concept_relation")]
public class StockConceptRelationEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>股票代码</summary>
    [Column("stock_code")]
    [StringLength(20)]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>概念名称</summary>
    [Column("concept_name")]
    [StringLength(100)]
    public string ConceptName { get; set; } = string.Empty;

    /// <summary>板块代码（东财 NEW_BOARD_CODE，如 BK0809）</summary>
    [Column("quote_code")]
    [StringLength(50)]
    public string? QuoteCode { get; set; }

    /// <summary>概念ID（同花顺 concept_id，东财来源为空）</summary>
    [Column("concept_id")]
    public int? ConceptId { get; set; }

    /// <summary>题材排名（东财 BOARD_RANK，越小越核心）</summary>
    [Column("board_rank")]
    public int? BoardRank { get; set; }

    /// <summary>入选理由（东财 SELECTED_BOARD_REASON）</summary>
    [Column("selected_reason", TypeName = "text")]
    public string? SelectedReason { get; set; }

    /// <summary>LLM 蒸馏的"炒作点"短语（≤12字，从 selected_reason 提炼，供选股展示）。null=未蒸馏。</summary>
    [Column("concept_digest")]
    [StringLength(60)]
    public string? ConceptDigest { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
