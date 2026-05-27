using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 公司关系实体
/// </summary>
[Table("company_relation")]
public class CompanyRelationEntity
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// 源公司代码
    /// </summary>
    [Column("source_company")]
    [StringLength(20)]
    public string SourceCompany { get; set; } = string.Empty;

    /// <summary>
    /// 目标公司代码
    /// </summary>
    [Column("target_company")]
    [StringLength(20)]
    public string TargetCompany { get; set; } = string.Empty;

    /// <summary>
    /// 关系类型（customer/supplier/invest/controll）
    /// </summary>
    [Column("relation_type")]
    [StringLength(50)]
    public string RelationType { get; set; } = string.Empty;

    /// <summary>
    /// 权重
    /// </summary>
    [Column("weight")]
    public decimal Weight { get; set; } = 1.0m;

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

    /// <summary>
    /// 更新时间
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
