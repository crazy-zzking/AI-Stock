using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Execution.Services;

/// <summary>
/// 交易闸门实现 — 单例，线程安全。管理 kill-switch 与每日下单计数。
/// 熔断/日单数/个股暂停经 <see cref="ITradingGateStore"/> 持久化（懒加载+写穿），
/// 进程重启不丢失；store 为 null 或故障时退化为纯内存（行为同旧版）。
/// </summary>
public class TradingGate : ITradingGate
{
    private readonly TradingGuardOptions _options;
    private readonly ILogger<TradingGate> _logger;
    private readonly ITradingGateStore? _store;
    private readonly object _lock = new();

    private bool _stateLoaded;
    private bool _runtimeHalted;
    private string? _haltReason;
    private DateOnly _countDate = DateOnly.FromDateTime(DateTime.Now);
    private int _orderCount;

    // 在途买入预留（金额 + 过期时刻），用于跨并行订单的原子敞口控制（TTL 短，不持久化）
    private readonly List<(DateTime Expiry, decimal Amount)> _buyReservations = new();

    // 股票级别暂停：code → 解禁时刻
    private Dictionary<string, DateTime> _stockSuspensions = new();

    // 连续下单失败计数：code → 失败次数（短期信号，不持久化）
    private readonly Dictionary<string, int> _failureCounts = new();

    private const int FailureThreshold = 3;
    private static readonly TimeSpan DefaultSuspendDuration = TimeSpan.FromMinutes(30);

    public TradingGate(IOptions<TradingGuardOptions> options, ILogger<TradingGate> logger,
        ITradingGateStore? store = null)
    {
        _options = options.Value;
        _logger = logger;
        _store = store;

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
            lock (_lock)
            {
                EnsureLoaded();
                return _runtimeHalted || _options.Halted;
            }
        }
    }

    public void Halt(string reason)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _runtimeHalted = true;
            _haltReason = reason;
            Persist();
        }
        _logger.LogWarning("交易已熔断 (kill-switch): {Reason}", reason);
    }

    public void Resume()
    {
        lock (_lock)
        {
            EnsureLoaded();
            _runtimeHalted = false;
            Persist();
        }
        _logger.LogWarning("交易熔断已解除");
    }

    public int TodayOrderCount
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                RollDateIfNeeded();
                return _orderCount;
            }
        }
    }

    public bool TryReserveOrderSlot(out string? rejectReason)
    {
        lock (_lock)
        {
            EnsureLoaded();
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
            Persist();
            rejectReason = null;
            return true;
        }
    }

    public bool TryReserveBuyValue(decimal availableBalance, decimal orderValue, out string? rejectReason)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
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

    public bool IsStockAllowed(string code, out string? rejectReason)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (_stockSuspensions.TryGetValue(code, out var until) && DateTime.Now < until)
            {
                rejectReason = $"{code} 已被暂停交易，解禁时间: {until:HH:mm:ss}";
                return false;
            }
            rejectReason = null;
            return true;
        }
    }

    public void SuspendStock(string code, TimeSpan duration, string reason)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var until = DateTime.Now.Add(duration);
            _stockSuspensions[code] = until;
            _failureCounts.Remove(code);
            Persist();
        }
        _logger.LogWarning("股票 {Code} 已被暂停交易 {Minutes} 分钟，原因: {Reason}", code, duration.TotalMinutes, reason);
    }

    public void RecordOrderFailure(string code)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _failureCounts.TryGetValue(code, out var count);
            count++;
            _failureCounts[code] = count;

            if (count >= FailureThreshold)
            {
                var until = DateTime.Now.Add(DefaultSuspendDuration);
                _stockSuspensions[code] = until;
                _failureCounts.Remove(code);
                Persist();
                _logger.LogWarning("股票 {Code} 连续下单失败 {Count} 次，自动暂停 {Minutes} 分钟",
                    code, FailureThreshold, DefaultSuspendDuration.TotalMinutes);
            }
        }
    }

    public void RecordOrderSuccess(string code)
    {
        lock (_lock)
        {
            _failureCounts.Remove(code);
        }
    }

    /// <summary>首次访问时从 store 恢复状态（持锁调用）。过期暂停顺手清理。</summary>
    private void EnsureLoaded()
    {
        if (_stateLoaded) return;
        _stateLoaded = true;
        if (_store == null) return;

        var state = _store.Load();
        if (state == null) return;

        _runtimeHalted = state.Halted;
        _haltReason = state.HaltReason;
        _countDate = state.CountDate;
        _orderCount = state.OrderCount;
        var now = DateTime.Now;
        _stockSuspensions = state.StockSuspensions
            .Where(kv => kv.Value > now)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (_runtimeHalted)
            _logger.LogWarning("交易闸门从持久化状态恢复：熔断中 (原因: {Reason})", _haltReason ?? "未记录");
        RollDateIfNeeded();
    }

    /// <summary>把当前状态写穿到 store（持锁调用；store 自吞异常不会抛出）。</summary>
    private void Persist()
    {
        _store?.Save(new TradingGateState
        {
            Halted = _runtimeHalted,
            HaltReason = _haltReason,
            CountDate = _countDate,
            OrderCount = _orderCount,
            StockSuspensions = new Dictionary<string, DateTime>(_stockSuspensions),
        });
    }

    private void RollDateIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _countDate)
        {
            _countDate = today;
            _orderCount = 0;
            Persist();
        }
    }
}
