using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 选股结果历史 — 每次选股追加一条记录（不覆盖），用 run_at 记录选股时间。
/// /latest 取最近一条，盘中刷新结果不跳动，直到下次重新选股再追加新记录。
/// 一行 = 一次选股批次（ResultsJson 存 List&lt;StockSelectionResult&gt; 序列化）。
/// </summary>
[Table("selection_result")]
public class SelectionResultEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>选股所基于的快照交易日</summary>
    [Column("trading_date")]
    public DateTime TradingDate { get; set; }

    /// <summary>生成时间（UTC）</summary>
    [Column("run_at")]
    public DateTime RunAt { get; set; }

    /// <summary>返回的 TOP-N</summary>
    [Column("top_n")]
    public int TopN { get; set; }

    /// <summary>选股所用策略键（lowdip/trend/theme/...；导入的记 import）</summary>
    [Column("strategy")]
    [StringLength(50)]
    public string Strategy { get; set; } = string.Empty;

    /// <summary>策略显示名（写入时固化，避免日后改名导致历史错乱）</summary>
    [Column("strategy_name")]
    [StringLength(100)]
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>选股时生效的配置版本（写入时固化：vX.Y / default=代码默认 / custom=显式传参）——策略迭代前后成绩分段对照的依据</summary>
    [Column("config_version")]
    [StringLength(50)]
    public string ConfigVersion { get; set; } = string.Empty;

    /// <summary>选股结果列表 JSON（List&lt;StockSelectionResult&gt;）</summary>
    [Column("results_json", TypeName = "longtext")]
    public string ResultsJson { get; set; } = "[]";

    /// <summary>LLM 复评状态：pending（待复评）/ running（复评中）/ done（已完成）/ failed（失败）/ skipped（未启用）</summary>
    [Column("review_status")]
    [StringLength(20)]
    public string ReviewStatus { get; set; } = "pending";

    /// <summary>复评完成时间（null = 未完成）</summary>
    [Column("reviewed_at")]
    public DateTime? ReviewedAt { get; set; }
}
