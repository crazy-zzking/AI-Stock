using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 股票基础信息实体
/// </summary>
[Table("stock_base")]
public class StockBaseEntity
{
    /// <summary>
    /// 股票代码
    /// </summary>
    [Key]
    [Column("code")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    [Column("name")]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 市场（sh/sz/bj）
    /// </summary>
    [Column("market")]
    [StringLength(10)]
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 行业
    /// </summary>
    [Column("industry")]
    [StringLength(100)]
    public string? Industry { get; set; }

    /// <summary>
    /// 上市日期
    /// </summary>
    [Column("list_date")]
    public DateTime? ListDate { get; set; }

    /// <summary>
    /// 是否退市
    /// </summary>
    [Column("is_delisted")]
    public bool IsDelisted { get; set; }

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
