using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 知识图谱候选边 — 来自小作文/情报的 LLM 推断关系，带可信度与来源，
/// 与权威图谱（CompanyRelation/IndustryChain）隔离，仅作线索；高可信度才晋升。
/// </summary>
[Table("graph_candidate_edge")]
public class GraphCandidateEdgeEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>源实体（公司名/代码）</summary>
    [Column("from_entity")]
    [StringLength(100)]
    public string FromEntity { get; set; } = string.Empty;

    /// <summary>目标实体（公司名/概念名）</summary>
    [Column("to_entity")]
    [StringLength(100)]
    public string ToEntity { get; set; } = string.Empty;

    /// <summary>边类型：concept(公司-概念) / co-occur(公司-公司共现)</summary>
    [Column("edge_type")]
    [StringLength(30)]
    public string EdgeType { get; set; } = string.Empty;

    /// <summary>可信度（来自小作文分析的 CredibilityScore，0-100）</summary>
    [Column("credibility")]
    public int Credibility { get; set; }

    /// <summary>被提及次数（多次出现累加，越多越可信）</summary>
    [Column("mention_count")]
    public int MentionCount { get; set; } = 1;

    /// <summary>最近来源 URL</summary>
    [Column("last_source_url")]
    [StringLength(500)]
    public string? LastSourceUrl { get; set; }

    /// <summary>是否已晋升到权威图谱</summary>
    [Column("promoted")]
    public bool Promoted { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
