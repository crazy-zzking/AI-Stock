namespace AIStock.Web;

/// <summary>
/// 尾盘自动下单配置。默认关闭 + 仅 DryRun（防呆，不会误花真钱）。
/// </summary>
public class TailBuyOptions
{
    public const string SectionName = "TailBuy";

    /// <summary>是否启用尾盘自动下单（默认关闭）</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>选股策略键（默认低吸 lowdip，用其当前生效配置）</summary>
    public string Strategy { get; set; } = "lowdip";

    /// <summary>买入只数上限（取选股 TOP-N 的前 N）</summary>
    public int TopN { get; set; } = 5;

    /// <summary>每只买入金额（元），按此金额向下取整到整手（100 股）</summary>
    public decimal PerStockValue { get; set; } = 20000m;

    /// <summary>尾盘决策时点(HH:mm)，应与盘中快照刷新对齐(MarketSnapshot:CloseDecisionTime)一致</summary>
    public string DecisionTime { get; set; } = "14:55";

    /// <summary>触发窗口分钟数（自 DecisionTime 起的容差，避免错过）</summary>
    public int WindowMinutes { get; set; } = 10;

    /// <summary>额外保险：即便 TradingGate=Live 也只走计划不真下单（默认 true）</summary>
    public bool DryRunOnly { get; set; } = true;
}
