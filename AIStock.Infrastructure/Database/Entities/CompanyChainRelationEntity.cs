using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 公司-产业链关联实体
/// </summary>
[Table("company_chain_relation")]
public class CompanyChainRelationEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 公司代码
    /// </summary>
    [Column("company_code")]
    [StringLength(20)]
    public string CompanyCode { get; set; } = string.Empty;

    /// <summary>
    /// 产业链ID
    /// </summary>
    [Column("chain_id")]
    public long ChainId { get; set; }

    /// <summary>
    /// 在产业链中的节点名称
    /// </summary>
    [Column("chain_node")]
    [StringLength(100)]
    public string ChainNode { get; set; } = string.Empty;

    /// <summary>
    /// 角色（upstream/midstream/downstream）
    /// </summary>
    [Column("role")]
    [StringLength(50)]
    public string? Role { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
