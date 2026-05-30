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

    /// <summary>概念来源行情代码（同花顺 quote_code）</summary>
    [Column("quote_code")]
    [StringLength(50)]
    public string? QuoteCode { get; set; }

    /// <summary>概念ID（同花顺 concept_id）</summary>
    [Column("concept_id")]
    public int? ConceptId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
