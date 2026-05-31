using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 交易闸门 — 统一管理 kill-switch 与每日下单计数，供所有下单路径共享。
/// </summary>
public interface ITradingGate
{
    /// <summary>
    /// 当前交易模式
    /// </summary>
    TradingMode Mode { get; }

    /// <summary>
    /// 是否已熔断（kill-switch 触发或配置 Halted）
    /// </summary>
    bool IsHalted { get; }

    /// <summary>
    /// 运行时触发熔断，立即停止所有自动交易。
    /// </summary>
    void Halt(string reason);

    /// <summary>
    /// 解除熔断。
    /// </summary>
    void Resume();

    /// <summary>
    /// 今日已下单次数（自然日，按本地交易日重置）。
    /// </summary>
    int TodayOrderCount { get; }

    /// <summary>
    /// 校验是否允许再下一单（含每日次数上限）。不通过返回 false 与原因。
    /// </summary>
    bool TryReserveOrderSlot(out string? rejectReason);

    /// <summary>
    /// 原子预留买入金额：将在途未结买单的累计金额一并计入，防止并行下单叠加超出可用资金/总敞口上限。
    /// 通过则记入预留（带 TTL 自动释放）并返回 true，否则返回 false 与原因。
    /// </summary>
    bool TryReserveBuyValue(decimal availableBalance, decimal orderValue, out string? rejectReason);

    /// <summary>
    /// 记录指定股票下单失败。连续失败达到阈值后自动暂停该股票一段时间。
    /// </summary>
    void RecordOrderFailure(string code);

    /// <summary>
    /// 记录指定股票下单成功（重置连续失败计数）。
    /// </summary>
    void RecordOrderSuccess(string code);

    /// <summary>
    /// 检查指定股票是否被暂停交易（因连续失败或涨跌停熔断）。
    /// 不通过返回 false 与原因。
    /// </summary>
    bool IsStockAllowed(string code, out string? rejectReason);

    /// <summary>
    /// 手动暂停指定股票交易一段时间（用于极端行情熔断）。
    /// </summary>
    void SuspendStock(string code, TimeSpan duration, string reason);
}
