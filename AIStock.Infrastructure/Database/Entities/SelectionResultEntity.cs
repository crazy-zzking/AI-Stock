using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 选股结果快照 — 每个交易日冻结一份 TOP-N 选股结果，避免盘中每次刷新都重算导致结果跳动。
/// 一行 = 一次选股批次（ResultsJson 存 List&lt;StockSelectionResult&gt; 序列化）。唯一键 trading_date。
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

    /// <summary>选股结果列表 JSON（List&lt;StockSelectionResult&gt;）</summary>
    [Column("results_json", TypeName = "longtext")]
    public string ResultsJson { get; set; } = "[]";
}
