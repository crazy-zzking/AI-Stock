using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Execution.Services;

/// <summary>
/// 交易闸门实现 — 单例，线程安全。管理 kill-switch 与每日下单计数。
/// </summary>
public class TradingGate : ITradingGate
{
    private readonly TradingGuardOptions _options;
    private readonly ILogger<TradingGate> _logger;
    private readonly object _lock = new();

    private bool _runtimeHalted;
    private DateOnly _countDate = DateOnly.FromDateTime(DateTime.Now);
    private int _orderCount;

    public TradingGate(IOptions<TradingGuardOptions> options, ILogger<TradingGate> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (_options.Mode == TradingMode.Live)
            _logger.LogWarning("TradingGate 已启用【实盘 Live】模式 — 将下真实订单");
        else
            _logger.LogInformation("TradingGate 处于【模拟 DryRun】模式 — 不会调用券商接口");
    }

    public TradingMode Mode => _options.Mode;

    public bool IsHalted
    {
        get
        {
            lock (_lock) return _runtimeHalted || _options.Halted;
        }
    }

    public void Halt(string reason)
    {
        lock (_lock) _runtimeHalted = true;
        _logger.LogWarning("交易已熔断 (kill-switch): {Reason}", reason);
    }

    public void Resume()
    {
        lock (_lock) _runtimeHalted = false;
        _logger.LogWarning("交易熔断已解除");
    }

    public int TodayOrderCount
    {
        get
        {
            lock (_lock)
            {
                RollDateIfNeeded();
                return _orderCount;
            }
        }
    }

    public bool TryReserveOrderSlot(out string? rejectReason)
    {
        lock (_lock)
        {
            if (_runtimeHalted || _options.Halted)
            {
                rejectReason = "交易已熔断 (kill-switch)";
                return false;
            }

            RollDateIfNeeded();

            if (_options.MaxOrdersPerDay > 0 && _orderCount >= _options.MaxOrdersPerDay)
            {
                rejectReason = $"已达每日下单上限 ({_options.MaxOrdersPerDay})";
                return false;
            }

            _orderCount++;
            rejectReason = null;
            return true;
        }
    }

    private void RollDateIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _countDate)
        {
            _countDate = today;
            _orderCount = 0;
        }
    }
}
