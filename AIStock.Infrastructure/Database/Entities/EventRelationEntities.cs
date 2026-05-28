using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 事件-股票关联实体
/// </summary>
[Table("event_stock_relation")]
public class EventStockRelationEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 事件ID
    /// </summary>
    [Column("event_id")]
    public long EventId { get; set; }

    /// <summary>
    /// 股票代码
    /// </summary>
    [Column("stock_code")]
    [StringLength(20)]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 导航属性 - 事件记录
    /// </summary>
    [ForeignKey("EventId")]
    public virtual EventRecordEntity? Event { get; set; }
}

/// <summary>
/// 事件-概念关联实体
/// </summary>
[Table("event_concept_relation")]
public class EventConceptRelationEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 事件ID
    /// </summary>
    [Column("event_id")]
    public long EventId { get; set; }

    /// <summary>
    /// 概念名称
    /// </summary>
    [Column("concept_name")]
    [StringLength(100)]
    public string ConceptName { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 导航属性 - 事件记录
    /// </summary>
    [ForeignKey("EventId")]
    public virtual EventRecordEntity? Event { get; set; }
}
