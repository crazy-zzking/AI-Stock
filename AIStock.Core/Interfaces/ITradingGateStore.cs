namespace AIStock.Core.Interfaces;

/// <summary>
/// 交易闸门状态持久化存储。让 kill-switch / 日单数 / 个股暂停在进程重启后不丢失，
/// 消除"盘中熔断后服务重启即自动解除"的风险。实现必须自吞异常（闸门不能因存储故障而失效）。
/// </summary>
public interface ITradingGateStore
{
    /// <summary>加载最近一次保存的状态；无记录或加载失败返回 null（闸门按初始状态运行）。</summary>
    TradingGateState? Load();

    /// <summary>保存当前状态（覆盖式）。失败应记日志并静默返回。</summary>
    void Save(TradingGateState state);
}

/// <summary>交易闸门需要跨重启保持的状态快照。</summary>
public class TradingGateState
{
    /// <summary>运行时 kill-switch 是否处于熔断。</summary>
    public bool Halted { get; set; }

    /// <summary>最近一次熔断原因（解除后保留供追溯）。</summary>
    public string? HaltReason { get; set; }

    /// <summary>日单数计数所属日期。</summary>
    public DateOnly CountDate { get; set; }

    /// <summary>当日已下单数。</summary>
    public int OrderCount { get; set; }

    /// <summary>个股暂停名单：code → 解禁时刻（本地时间）。</summary>
    public Dictionary<string, DateTime> StockSuspensions { get; set; } = new();
}
