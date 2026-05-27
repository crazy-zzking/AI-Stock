using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 产业链实体
/// </summary>
[Table("industry_chain")]
public class IndustryChainEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 产业链名称
    /// </summary>
    [Column("chain_name")]
    [StringLength(100)]
    public string ChainName { get; set; } = string.Empty;

    /// <summary>
    /// 父节点
    /// </summary>
    [Column("parent_node")]
    [StringLength(100)]
    public string? ParentNode { get; set; }

    /// <summary>
    /// 子节点
    /// </summary>
    [Column("child_node")]
    [StringLength(100)]
    public string ChildNode { get; set; } = string.Empty;

    /// <summary>
    /// 层级
    /// </summary>
    [Column("level")]
    public int Level { get; set; }

    /// <summary>
    /// 节点类型（company/product/technology）
    /// </summary>
    [Column("node_type")]
    [StringLength(50)]
    public string? NodeType { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [Column("description")]
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
