using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 每日复盘记录 — 一交易日一份（唯一键 trading_date），report_json 存完整 DailyReviewReport。
/// 同时冗余几个常用统计列便于列表/检索。
/// </summary>
[Table("daily_review")]
public class DailyReviewEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("trading_date")]
    public DateTime TradingDate { get; set; }

    [Column("generated_at")]
    public DateTime GeneratedAt { get; set; }

    [Column("limit_up_count")]
    public int LimitUpCount { get; set; }

    [Column("up_count")]
    public int UpCount { get; set; }

    [Column("down_count")]
    public int DownCount { get; set; }

    /// <summary>完整复盘报告 JSON（DailyReviewReport）</summary>
    [Column("report_json", TypeName = "longtext")]
    public string ReportJson { get; set; } = "{}";
}
