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

    // 在途买入预留（金额 + 过期时刻），用于跨并行订单的原子敞口控制
    private readonly List<(DateTime Expiry, decimal Amount)> _buyReservations = new();

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

    public bool TryReserveBuyValue(decimal availableBalance, decimal orderValue, out string? rejectReason)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _buyReservations.RemoveAll(r => r.Expiry <= now);
            var reserved = _buyReservations.Sum(r => r.Amount);

            // 1. 可用资金约束（含在途预留）
            if (orderValue > availableBalance - reserved)
            {
                rejectReason = $"可用资金不足(含在途预留): 需 {orderValue:N0}，可用 {availableBalance:N0}，已预留 {reserved:N0}";
                return false;
            }

            // 2. 总敞口上限（可选）
            if (_options.MaxTotalExposure > 0 && reserved + orderValue > _options.MaxTotalExposure)
            {
                rejectReason = $"超过总买入敞口上限: 已预留 {reserved:N0} + 本单 {orderValue:N0} > {_options.MaxTotalExposure:N0}";
                return false;
            }

            var ttl = TimeSpan.FromSeconds(_options.ReservationTtlSeconds > 0 ? _options.ReservationTtlSeconds : 120);
            _buyReservations.Add((now.Add(ttl), orderValue));
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
