using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStock.Infrastructure.Database.Entities;

/// <summary>
/// 交易闸门持久化状态（单行表，Id 恒为 1）。kill-switch / 日单数 / 个股暂停跨进程重启保持，
/// 防止"盘中熔断后重启即自动解除"。
/// </summary>
[Table("trading_gate_state")]
public class TradingGateStateEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; } = 1;

    /// <summary>运行时 kill-switch 是否熔断</summary>
    [Column("halted")]
    public bool Halted { get; set; }

    /// <summary>最近一次熔断原因</summary>
    [Column("halt_reason")]
    [StringLength(500)]
    public string? HaltReason { get; set; }

    /// <summary>日单数计数所属日期</summary>
    [Column("count_date")]
    public DateTime CountDate { get; set; }

    /// <summary>当日已下单数</summary>
    [Column("order_count")]
    public int OrderCount { get; set; }

    /// <summary>个股暂停名单 JSON（code → 解禁时刻）</summary>
    [Column("suspensions_json", TypeName = "text")]
    public string SuspensionsJson { get; set; } = "{}";

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
