using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Execution.Services;
using AIStock.Risk.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AIStock.Tests;

public class TradingGateCircuitBreakerTests
{
    private static TradingGate Create() =>
        new TradingGate(
            Options.Create(new TradingGuardOptions { Mode = TradingMode.DryRun, MaxOrdersPerDay = 50 }),
            NullLogger<TradingGate>.Instance);

    // ===== 股票暂停：手动 =====

    [Fact]
    public void SuspendStock_BlocksThatStock()
    {
        var gate = Create();

        gate.SuspendStock("000001", TimeSpan.FromMinutes(30), "测试");

        Assert.False(gate.IsStockAllowed("000001", out var reason));
        Assert.Contains("暂停", reason);
    }

    [Fact]
    public void SuspendStock_DoesNotAffectOtherStocks()
    {
        var gate = Create();

        gate.SuspendStock("000001", TimeSpan.FromMinutes(30), "测试");

        Assert.True(gate.IsStockAllowed("600036", out _));
    }

    [Fact]
    public void SuspendStock_ExpiredSuspension_AllowsStock()
    {
        var gate = Create();

        // 暂停 1ms，马上就过期
        gate.SuspendStock("000001", TimeSpan.FromMilliseconds(1), "测试");
        Thread.Sleep(10);

        Assert.True(gate.IsStockAllowed("000001", out _));
    }

    // ===== 连续失败自动暂停 =====

    [Fact]
    public void RecordOrderFailure_BelowThreshold_StockStillAllowed()
    {
        var gate = Create();

        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001");

        Assert.True(gate.IsStockAllowed("000001", out _));
    }

    [Fact]
    public void RecordOrderFailure_ReachesThreshold_StockSuspended()
    {
        var gate = Create();

        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001"); // 第3次触发

        Assert.False(gate.IsStockAllowed("000001", out var reason));
        Assert.Contains("暂停", reason);
    }

    [Fact]
    public void RecordOrderSuccess_ResetsFailureCount()
    {
        var gate = Create();

        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001");
        gate.RecordOrderSuccess("000001"); // 重置
        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001"); // 再失败2次（未达阈值）

        Assert.True(gate.IsStockAllowed("000001", out _));
    }

    [Fact]
    public void RecordOrderFailure_DoesNotAffectOtherStocks()
    {
        var gate = Create();

        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001");
        gate.RecordOrderFailure("000001");

        Assert.True(gate.IsStockAllowed("600036", out _));
    }

    // ===== 默认状态 =====

    [Fact]
    public void IsStockAllowed_DefaultState_AllowsAnyStock()
    {
        var gate = Create();

        Assert.True(gate.IsStockAllowed("000001", out _));
        Assert.True(gate.IsStockAllowed("600036", out _));
    }
}

public class ExtremeDeclineRiskTests
{
    private static RiskEngineService CreateService() =>
        new RiskEngineService(NullLogger<RiskEngineService>.Instance, new RiskConfig
        {
            MaxDeclinePercent = 9m,
            MaxSingleTradePercent = 10m,
            MaxTotalPositionPercent = 80m,
            MaxSingleStockPercent = 20m,
            MaxSectorPercent = 40m,
            MediumThreshold = 1,
            HighThreshold = 2,
            CriticalThreshold = 3
        });

    private static TradeSignal BuySignal(decimal changePercent) => new()
    {
        Code = "000001",
        SignalType = SignalType.Buy,
        Price = 10m,
        Volume = 500,
        ChangePercent = changePercent
    };

    // ===== 极端下跌禁买 =====

    [Fact]
    public async Task CheckRisk_BuyOnExtremeDecline_Fails()
    {
        var svc = CreateService();
        var signal = BuySignal(-9.5m); // 跌 9.5% > 阈值 9%

        var result = await svc.CheckRiskAsync(signal, new(), 100_000m);

        Assert.False(result.Passed);
        var check = result.Checks.FirstOrDefault(c => c.Name == "极端下跌禁买");
        Assert.NotNull(check);
        Assert.False(check.Passed);
    }

    [Fact]
    public async Task CheckRisk_BuyOnNormalDecline_Passes()
    {
        var svc = CreateService();
        var signal = BuySignal(-3m); // 跌 3%，正常

        var result = await svc.CheckRiskAsync(signal, new(), 100_000m);

        var check = result.Checks.FirstOrDefault(c => c.Name == "极端下跌禁买");
        Assert.NotNull(check);
        Assert.True(check.Passed);
    }

    [Fact]
    public async Task CheckRisk_BuyOnExactThreshold_IsBlocked()
    {
        var svc = CreateService();
        // 跌幅恰好达到阈值 -9%：接近跌停，可能是主力砸盘出货，无法仅凭涨跌幅区分买盘/砸盘，保守拦截（>= 触发）
        var signal = BuySignal(-9m);

        var result = await svc.CheckRiskAsync(signal, new(), 100_000m);

        var check = result.Checks.FirstOrDefault(c => c.Name == "极端下跌禁买");
        Assert.NotNull(check);
        Assert.False(check.Passed);
    }

    [Fact]
    public async Task CheckRisk_SellSignal_NoExtremeDeclineCheck()
    {
        var svc = CreateService();
        var signal = new TradeSignal
        {
            Code = "000001",
            SignalType = SignalType.Sell,
            Price = 10m,
            Volume = 500,
            ChangePercent = -9.5m // 卖出信号不应触发禁买检查
        };

        var result = await svc.CheckRiskAsync(signal, new(), 100_000m);

        var check = result.Checks.FirstOrDefault(c => c.Name == "极端下跌禁买");
        Assert.Null(check); // 卖出信号不添加此检查项
    }

    [Fact]
    public async Task CheckRisk_UnknownChangePercent_Passes()
    {
        var svc = CreateService();
        var signal = BuySignal(0m); // ChangePercent=0 视为未知，不触发

        var result = await svc.CheckRiskAsync(signal, new(), 100_000m);

        var check = result.Checks.FirstOrDefault(c => c.Name == "极端下跌禁买");
        Assert.NotNull(check);
        Assert.True(check.Passed);
    }
}

public class OrderManagerCircuitBreakerTests
{
    private readonly Mock<IDataProvider> _providerMock = new();
    private readonly Mock<IDataProviderResolver> _resolverMock = new();

    private (OrderManagerService svc, TradingGate gate) CreateService(TradingMode mode = TradingMode.DryRun)
    {
        _resolverMock
            .Setup(r => r.GetPrimaryProvider(DataCapability.Trading))
            .Returns(_providerMock.Object);

        var opts = new TradingGuardOptions { Mode = mode, MaxOrdersPerDay = 50 };
        var gate = new TradingGate(Options.Create(opts), NullLogger<TradingGate>.Instance);
        var svc = new OrderManagerService(
            _resolverMock.Object, gate,
            Options.Create(opts),
            NullLogger<OrderManagerService>.Instance);
        return (svc, gate);
    }

    private void SetupAccount(decimal balance = 100_000m)
    {
        _providerMock.Setup(p => p.GetAccountInfoAsync()).ReturnsAsync(new AccountInfo
        {
            TotalAssets = balance,
            AvailableBalance = balance,
            Positions = new()
        });
    }

    [Fact]
    public async Task PlaceOrder_SuspendedStock_Rejected()
    {
        var (svc, gate) = CreateService();
        SetupAccount();
        gate.SuspendStock("000001", TimeSpan.FromMinutes(30), "测试暂停");

        var result = await svc.PlaceOrderAsync(new OrderRequest
        {
            Code = "000001", Side = "buy", Price = 10m, Volume = 1000, OrderType = OrderType.Limit
        });

        Assert.False(result.Success);
        Assert.Contains("暂停", result.Message);
    }

    [Fact]
    public async Task PlaceOrder_LiveFailure_RecordsFailureAndSuspendsAfterThreshold()
    {
        var (svc, gate) = CreateService(TradingMode.Live);
        SetupAccount();
        _providerMock
            .Setup(p => p.PlaceBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<int>()))
            .ReturnsAsync(new TradingOrderResult { IsFailed = true, Msg = "券商拒单" });

        var order = new OrderRequest { Code = "000001", Side = "buy", Price = 10m, Volume = 1000, OrderType = OrderType.Limit };

        await svc.PlaceOrderAsync(order);
        await svc.PlaceOrderAsync(order);
        await svc.PlaceOrderAsync(order); // 第3次失败 → 自动暂停

        Assert.False(gate.IsStockAllowed("000001", out _));
    }
}
