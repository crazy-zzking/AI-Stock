using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AIStock.Tests;

public class OrderManagerServiceTests
{
    private readonly Mock<IDataProvider> _providerMock = new();
    private readonly Mock<IDataProviderResolver> _resolverMock = new();

    private OrderManagerService CreateService(
        TradingMode mode = TradingMode.DryRun,
        decimal maxOrderValue = 100_000m,
        int maxOrdersPerDay = 50,
        bool halted = false)
    {
        _resolverMock
            .Setup(r => r.GetPrimaryProvider(DataCapability.Trading))
            .Returns(_providerMock.Object);

        var opts = new TradingGuardOptions
        {
            Mode = mode,
            MaxOrderValue = maxOrderValue,
            MaxOrdersPerDay = maxOrdersPerDay,
            Halted = halted
        };
        var gate = new TradingGate(Options.Create(opts), NullLogger<TradingGate>.Instance);

        return new OrderManagerService(
            _resolverMock.Object,
            gate,
            Options.Create(opts),
            NullLogger<OrderManagerService>.Instance);
    }

    private static OrderRequest BuyOrder(decimal price = 10m, long volume = 1000) => new()
    {
        Code = "000001",
        Side = "buy",
        Price = price,
        Volume = volume,
        OrderType = OrderType.Limit
    };

    private static OrderRequest SellOrder(decimal price = 10m, long volume = 1000) => new()
    {
        Code = "000001",
        Side = "sell",
        Price = price,
        Volume = volume,
        OrderType = OrderType.Limit
    };

    private void SetupAccount(decimal balance = 100_000m, string? positionCode = null, long availableVolume = 0)
    {
        var account = new AccountInfo
        {
            TotalAssets = balance,
            AvailableBalance = balance,
            Positions = positionCode != null
                ? new List<AccountPosition>
                {
                    new() { Code = positionCode, Volume = availableVolume, AvailableVolume = availableVolume }
                }
                : new List<AccountPosition>()
        };
        _providerMock.Setup(p => p.GetAccountInfoAsync()).ReturnsAsync(account);
    }

    // ===== 数量校验 =====

    [Fact]
    public async Task PlaceOrder_VolumeLessThan100_Rejected()
    {
        var svc = CreateService();
        SetupAccount();

        var result = await svc.PlaceOrderAsync(BuyOrder(volume: 50));

        Assert.False(result.Success);
        Assert.Contains("100股", result.Message);
        Assert.Equal(OrderStatus.Failed, result.Status);
    }

    [Fact]
    public async Task PlaceOrder_VolumeExactly100_Allowed()
    {
        var svc = CreateService(); // DryRun
        SetupAccount(balance: 100_000m);

        var result = await svc.PlaceOrderAsync(BuyOrder(price: 10m, volume: 100));

        Assert.True(result.Success);
    }

    // ===== 单笔金额上限 =====

    [Fact]
    public async Task PlaceOrder_ExceedsMaxOrderValue_Rejected()
    {
        var svc = CreateService(maxOrderValue: 50_000m);
        SetupAccount();

        // 10元 × 6000股 = 60000 > 50000
        var result = await svc.PlaceOrderAsync(BuyOrder(price: 10m, volume: 6000));

        Assert.False(result.Success);
        Assert.Contains("单笔金额", result.Message);
    }

    [Fact]
    public async Task PlaceOrder_WithinMaxOrderValue_Passes()
    {
        var svc = CreateService(maxOrderValue: 50_000m); // DryRun
        SetupAccount(balance: 100_000m);

        // 10元 × 1000股 = 10000 < 50000
        var result = await svc.PlaceOrderAsync(BuyOrder(price: 10m, volume: 1000));

        Assert.True(result.Success);
    }

    // ===== Kill-Switch =====

    [Fact]
    public async Task PlaceOrder_WhenHalted_Rejected()
    {
        var svc = CreateService(halted: true);
        SetupAccount();

        var result = await svc.PlaceOrderAsync(BuyOrder());

        Assert.False(result.Success);
        Assert.Contains("拒绝", result.Message);
    }

    // ===== DryRun 模式 =====

    [Fact]
    public async Task PlaceOrder_DryRunMode_ReturnsDryRunOrderId()
    {
        var svc = CreateService(mode: TradingMode.DryRun);
        SetupAccount(balance: 100_000m);

        var result = await svc.PlaceOrderAsync(BuyOrder());

        Assert.True(result.Success);
        Assert.StartsWith("DRYRUN-", result.OrderId);
        Assert.Contains("DryRun", result.Message);
        Assert.Equal(OrderStatus.Submitted, result.Status);
    }

    [Fact]
    public async Task PlaceOrder_DryRunMode_DoesNotCallBroker()
    {
        var svc = CreateService(mode: TradingMode.DryRun);
        SetupAccount(balance: 100_000m);

        await svc.PlaceOrderAsync(BuyOrder());

        _providerMock.Verify(p => p.PlaceBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<int>()), Times.Never);
    }

    // ===== 账户资金校验（买入） =====

    [Fact]
    public async Task PlaceOrder_BuyWithInsufficientBalance_Rejected()
    {
        var svc = CreateService(mode: TradingMode.DryRun);
        SetupAccount(balance: 1_000m); // 资金不足

        // 10元 × 1000股 = 10000 > 1000 余额
        var result = await svc.PlaceOrderAsync(BuyOrder(price: 10m, volume: 1000));

        Assert.False(result.Success);
        Assert.Contains("资金", result.Message);
    }

    // ===== 账户持仓校验（卖出） =====

    [Fact]
    public async Task PlaceOrder_SellWithInsufficientPosition_Rejected()
    {
        var svc = CreateService(mode: TradingMode.DryRun);
        SetupAccount(balance: 100_000m, positionCode: "000001", availableVolume: 500);

        // 尝试卖出 1000 股，可用只有 500
        var result = await svc.PlaceOrderAsync(SellOrder(volume: 1000));

        Assert.False(result.Success);
        Assert.Contains("持仓", result.Message);
    }

    [Fact]
    public async Task PlaceOrder_SellWithSufficientPosition_DryRunSucceeds()
    {
        var svc = CreateService(mode: TradingMode.DryRun);
        SetupAccount(balance: 100_000m, positionCode: "000001", availableVolume: 2000);

        var result = await svc.PlaceOrderAsync(SellOrder(volume: 1000));

        Assert.True(result.Success);
        Assert.StartsWith("DRYRUN-", result.OrderId);
    }

    // ===== Live 模式实际下单 =====

    [Fact]
    public async Task PlaceOrder_LiveMode_CallsBrokerBuy()
    {
        var svc = CreateService(mode: TradingMode.Live);
        SetupAccount(balance: 100_000m);
        _providerMock
            .Setup(p => p.PlaceBuyOrderAsync("000001", 10m, 1000))
            .ReturnsAsync(new TradingOrderResult { OrderId = 999, Msg = "委托成功", IsAccepted = true });

        var result = await svc.PlaceOrderAsync(BuyOrder(price: 10m, volume: 1000));

        Assert.True(result.Success);
        Assert.Equal("999", result.OrderId);
        Assert.Equal(OrderStatus.Submitted, result.Status);
        _providerMock.Verify(p => p.PlaceBuyOrderAsync("000001", 10m, 1000), Times.Once);
    }

    [Fact]
    public async Task PlaceOrder_LiveMode_CallsBrokerSell()
    {
        var svc = CreateService(mode: TradingMode.Live);
        SetupAccount(balance: 100_000m, positionCode: "000001", availableVolume: 2000);
        _providerMock
            .Setup(p => p.PlaceSellOrderAsync("000001", 10m, 1000))
            .ReturnsAsync(new TradingOrderResult { OrderId = 888, Msg = "委托成功", IsAccepted = true });

        var result = await svc.PlaceOrderAsync(SellOrder(price: 10m, volume: 1000));

        Assert.True(result.Success);
        Assert.Equal("888", result.OrderId);
        _providerMock.Verify(p => p.PlaceSellOrderAsync("000001", 10m, 1000), Times.Once);
    }

    [Fact]
    public async Task PlaceOrder_LiveMode_BrokerFails_ReturnsFailure()
    {
        var svc = CreateService(mode: TradingMode.Live);
        SetupAccount(balance: 100_000m);
        _providerMock
            .Setup(p => p.PlaceBuyOrderAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<int>()))
            .ReturnsAsync(new TradingOrderResult { OrderId = 0, Msg = "余额不足", IsFailed = true });

        var result = await svc.PlaceOrderAsync(BuyOrder());

        Assert.False(result.Success);
        Assert.Equal(OrderStatus.Failed, result.Status);
    }

    // ===== Provider 未配置 =====

    [Fact]
    public async Task PlaceOrder_NoProvider_Rejected()
    {
        _resolverMock
            .Setup(r => r.GetPrimaryProvider(DataCapability.Trading))
            .Returns((IDataProvider?)null);

        var opts = new TradingGuardOptions { Mode = TradingMode.DryRun };
        var gate = new TradingGate(Options.Create(opts), NullLogger<TradingGate>.Instance);
        var svc = new OrderManagerService(
            _resolverMock.Object, gate,
            Options.Create(opts),
            NullLogger<OrderManagerService>.Instance);

        var result = await svc.PlaceOrderAsync(BuyOrder());

        Assert.False(result.Success);
        Assert.Contains("Provider", result.Message);
    }

    // ===== 账户查询异常 =====

    [Fact]
    public async Task PlaceOrder_AccountQueryThrows_ConservativeReject()
    {
        var svc = CreateService(mode: TradingMode.DryRun);
        _providerMock
            .Setup(p => p.GetAccountInfoAsync())
            .ThrowsAsync(new Exception("网络超时"));

        var result = await svc.PlaceOrderAsync(BuyOrder());

        Assert.False(result.Success);
        Assert.Contains("账户查询失败", result.Message);
    }
}
