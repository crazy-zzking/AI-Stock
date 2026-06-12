using AIStock.Core.Models;
using AIStock.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIStock.Tests;

public class TradingGateTests
{
    private static TradingGate Create(Action<TradingGuardOptions>? configure = null)
    {
        var opts = new TradingGuardOptions
        {
            Mode = TradingMode.DryRun,
            MaxOrdersPerDay = 5,
            MaxOrderValue = 100_000m,
            ReservationTtlSeconds = 60
        };
        configure?.Invoke(opts);
        return new TradingGate(Options.Create(opts), NullLogger<TradingGate>.Instance);
    }

    // ===== Kill-Switch =====

    [Fact]
    public void Halt_BlocksOrderSlot()
    {
        var gate = Create();
        gate.Halt("测试熔断");

        var result = gate.TryReserveOrderSlot(out var reason);

        Assert.False(result);
        Assert.Contains("kill-switch", reason);
    }

    [Fact]
    public void Resume_AfterHalt_AllowsOrderSlot()
    {
        var gate = Create();
        gate.Halt("测试熔断");
        gate.Resume();

        var result = gate.TryReserveOrderSlot(out var reason);

        Assert.True(result);
        Assert.Null(reason);
    }

    [Fact]
    public void StaticHalt_InOptions_BlocksOrderSlot()
    {
        var gate = Create(o => o.Halted = true);

        var result = gate.TryReserveOrderSlot(out var reason);

        Assert.False(result);
        Assert.NotNull(reason);
    }

    // ===== 每日下单限制 =====

    [Fact]
    public void TryReserveOrderSlot_WithinLimit_Succeeds()
    {
        var gate = Create(o => o.MaxOrdersPerDay = 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(gate.TryReserveOrderSlot(out _));
        }
        Assert.Equal(3, gate.TodayOrderCount);
    }

    [Fact]
    public void TryReserveOrderSlot_ExceedsLimit_Rejected()
    {
        var gate = Create(o => o.MaxOrdersPerDay = 2);

        gate.TryReserveOrderSlot(out _);
        gate.TryReserveOrderSlot(out _);
        var result = gate.TryReserveOrderSlot(out var reason);

        Assert.False(result);
        Assert.Contains("上限", reason);
    }

    [Fact]
    public void TryReserveOrderSlot_ZeroLimit_NeverBlocked()
    {
        var gate = Create(o => o.MaxOrdersPerDay = 0);

        for (var i = 0; i < 100; i++)
        {
            Assert.True(gate.TryReserveOrderSlot(out _));
        }
    }

    // ===== 买入资金预留 =====

    [Fact]
    public void TryReserveBuyValue_InsufficientFunds_Rejected()
    {
        var gate = Create();

        var result = gate.TryReserveBuyValue(availableBalance: 10_000m, orderValue: 20_000m, out var reason);

        Assert.False(result);
        Assert.Contains("可用资金不足", reason);
    }

    [Fact]
    public void TryReserveBuyValue_SufficientFunds_Succeeds()
    {
        var gate = Create();

        var result = gate.TryReserveBuyValue(availableBalance: 100_000m, orderValue: 20_000m, out var reason);

        Assert.True(result);
        Assert.Null(reason);
    }

    [Fact]
    public void TryReserveBuyValue_ConcurrentOrders_UsesReservations()
    {
        var gate = Create();

        // 第一笔预留 60000
        Assert.True(gate.TryReserveBuyValue(availableBalance: 100_000m, orderValue: 60_000m, out _));
        // 第二笔再申请 50000，余额只有 40000，应拒绝
        var result = gate.TryReserveBuyValue(availableBalance: 100_000m, orderValue: 50_000m, out var reason);

        Assert.False(result);
        Assert.Contains("在途预留", reason);
    }

    [Fact]
    public void TryReserveBuyValue_MaxTotalExposure_Enforced()
    {
        var gate = Create(o => o.MaxTotalExposure = 50_000m);

        Assert.True(gate.TryReserveBuyValue(availableBalance: 200_000m, orderValue: 30_000m, out _));
        var result = gate.TryReserveBuyValue(availableBalance: 200_000m, orderValue: 30_000m, out var reason);

        Assert.False(result);
        Assert.Contains("敞口", reason);
    }

    [Fact]
    public void IsHalted_DefaultState_IsFalse()
    {
        var gate = Create();

        Assert.False(gate.IsHalted);
    }

    // ===== 状态持久化（重启恢复） =====

    /// <summary>内存版 store：模拟跨进程重启的持久化存储。</summary>
    private class FakeStore : AIStock.Core.Interfaces.ITradingGateStore
    {
        public AIStock.Core.Interfaces.TradingGateState? State;
        public int SaveCount;
        public AIStock.Core.Interfaces.TradingGateState? Load() => State;
        public void Save(AIStock.Core.Interfaces.TradingGateState state) { State = state; SaveCount++; }
    }

    private static TradingGate CreateWithStore(FakeStore store, Action<TradingGuardOptions>? configure = null)
    {
        var opts = new TradingGuardOptions
        {
            Mode = TradingMode.DryRun,
            MaxOrdersPerDay = 5,
            ReservationTtlSeconds = 60
        };
        configure?.Invoke(opts);
        return new TradingGate(Options.Create(opts), NullLogger<TradingGate>.Instance, store);
    }

    [Fact]
    public void Halt_SurvivesRestart()
    {
        var store = new FakeStore();
        CreateWithStore(store).Halt("盘中熔断");

        // 模拟进程重启：新实例从同一 store 恢复
        var restarted = CreateWithStore(store);

        Assert.True(restarted.IsHalted);
    }

    [Fact]
    public void Resume_SurvivesRestart()
    {
        var store = new FakeStore();
        var gate = CreateWithStore(store);
        gate.Halt("熔断");
        gate.Resume();

        var restarted = CreateWithStore(store);

        Assert.False(restarted.IsHalted);
    }

    [Fact]
    public void OrderCount_SurvivesRestart()
    {
        var store = new FakeStore();
        var gate = CreateWithStore(store, o => o.MaxOrdersPerDay = 3);
        Assert.True(gate.TryReserveOrderSlot(out _));
        Assert.True(gate.TryReserveOrderSlot(out _));
        Assert.True(gate.TryReserveOrderSlot(out _));

        // 重启后日单数不清零：第 4 单仍被拒
        var restarted = CreateWithStore(store, o => o.MaxOrdersPerDay = 3);

        Assert.Equal(3, restarted.TodayOrderCount);
        Assert.False(restarted.TryReserveOrderSlot(out var reason));
        Assert.Contains("上限", reason);
    }

    [Fact]
    public void StockSuspension_SurvivesRestart_AndExpiredOnesPruned()
    {
        var store = new FakeStore();
        var gate = CreateWithStore(store);
        gate.SuspendStock("600001", TimeSpan.FromMinutes(30), "连续失败");
        // 手工注入一条已过期的暂停，验证恢复时被清理
        store.State!.StockSuspensions["000002"] = DateTime.Now.AddMinutes(-1);

        var restarted = CreateWithStore(store);

        Assert.False(restarted.IsStockAllowed("600001", out _));   // 未过期 → 恢复
        Assert.True(restarted.IsStockAllowed("000002", out _));    // 已过期 → 清理
    }

    [Fact]
    public void NullStore_BehavesInMemory()
    {
        // 不带 store（旧行为/降级路径）：功能完整，仅不持久化
        var gate = Create();
        gate.Halt("x");
        Assert.True(gate.IsHalted);
        gate.Resume();
        Assert.False(gate.IsHalted);
    }
}
