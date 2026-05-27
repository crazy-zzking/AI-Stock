using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 事件记录实体
/// </summary>
[Table("event_record")]
public class EventRecordEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 事件类型（news/report/policy/rumor）
    /// </summary>
    [Column("event_type")]
    [StringLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// 标题
    /// </summary>
    [Column("title")]
    [StringLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    [Column("content")]
    public string? Content { get; set; }

    /// <summary>
    /// 来源
    /// </summary>
    [Column("source")]
    [StringLength(200)]
    public string? Source { get; set; }

    /// <summary>
    /// 原始URL
    /// </summary>
    [Column("url")]
    [StringLength(1000)]
    public string? Url { get; set; }

    /// <summary>
    /// 情绪（positive/negative/neutral）
    /// </summary>
    [Column("sentiment")]
    [StringLength(20)]
    public string? Sentiment { get; set; }

    /// <summary>
    /// 情绪分数（-1到1）
    /// </summary>
    [Column("sentiment_score")]
    public decimal? SentimentScore { get; set; }

    /// <summary>
    /// 重要程度（1-10）
    /// </summary>
    [Column("importance")]
    public int? Importance { get; set; }

    /// <summary>
    /// 可信度（1-10）
    /// </summary>
    [Column("credibility")]
    public int? Credibility { get; set; }

    /// <summary>
    /// 关联股票代码（逗号分隔）
    /// </summary>
    [Column("related_stocks")]
    [StringLength(1000)]
    public string? RelatedStocks { get; set; }

    /// <summary>
    /// 关联概念（逗号分隔）
    /// </summary>
    [Column("related_concepts")]
    [StringLength(1000)]
    public string? RelatedConcepts { get; set; }

    /// <summary>
    /// LLM分析结果
    /// </summary>
    [Column("llm_analysis")]
    public string? LLMAnalysis { get; set; }

    /// <summary>
    /// 事件时间
    /// </summary>
    [Column("event_time")]
    public DateTime? EventTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
