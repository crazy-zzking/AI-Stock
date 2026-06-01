using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 东财板块目录 — 概念/行业/地域 板块的全量清单（板块代码 + 名称 + 类型），
/// 来自 push2 clist（fs=m:90+t:1/2/3）。用于板块名校验、板块导航、概念聚类。
/// </summary>
[Table("concept_board")]
public class ConceptBoardEntity
{
    /// <summary>板块代码（如 BK0809）</summary>
    [Key]
    [Column("board_code")]
    [StringLength(20)]
    public string BoardCode { get; set; } = string.Empty;

    /// <summary>板块名称</summary>
    [Column("board_name")]
    [StringLength(100)]
    public string BoardName { get; set; } = string.Empty;

    /// <summary>板块类型：概念 / 行业 / 地域</summary>
    [Column("board_type")]
    [StringLength(10)]
    public string BoardType { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
