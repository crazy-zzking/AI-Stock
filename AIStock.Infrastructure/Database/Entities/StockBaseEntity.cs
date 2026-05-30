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

    /// <summary>公司全称</summary>
    [Column("company_name")]
    [StringLength(200)]
    public string? CompanyName { get; set; }

    /// <summary>子行业/细分行业</summary>
    [Column("sub_industry")]
    [StringLength(100)]
    public string? SubIndustry { get; set; }

    /// <summary>主营业务</summary>
    [Column("main_business", TypeName = "text")]
    public string? MainBusiness { get; set; }

    /// <summary>公司简介</summary>
    [Column("profile", TypeName = "text")]
    public string? Profile { get; set; }

    /// <summary>所在省份</summary>
    [Column("province")]
    [StringLength(50)]
    public string? Province { get; set; }

    /// <summary>公司官网</summary>
    [Column("website")]
    [StringLength(200)]
    public string? Website { get; set; }

    /// <summary>员工人数</summary>
    [Column("employee_count")]
    public int? EmployeeCount { get; set; }

    /// <summary>注册资本</summary>
    [Column("reg_capital")]
    public decimal? RegCapital { get; set; }

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
