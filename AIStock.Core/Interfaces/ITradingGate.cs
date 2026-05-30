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
}
