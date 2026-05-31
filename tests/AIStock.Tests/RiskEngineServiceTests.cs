using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Risk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStock.Tests;

public class RiskEngineServiceTests
{
    private static RiskEngineService CreateService(Action<RiskConfig>? configure = null)
    {
        var config = new RiskConfig
        {
            MaxSingleTradePercent = 10m,
            MaxTotalPositionPercent = 80m,
            MaxSingleStockPercent = 20m,
            MaxSectorPercent = 40m,
            MediumThreshold = 1,
            HighThreshold = 2,
            CriticalThreshold = 3
        };
        configure?.Invoke(config);
        return new RiskEngineService(NullLogger<RiskEngineService>.Instance, config);
    }

    private static TradeSignal MakeSignal(decimal price = 10m, long volume = 1000) =>
        new() { Code = "000001", Price = price, Volume = volume, SignalType = SignalType.Buy };

    private static List<PortfolioPosition> EmptyPositions() => new();

    // ===== 单笔交易限制 =====

    [Fact]
    public async Task CheckSingleTrade_WithinLimit_Passes()
    {
        var svc = CreateService();
        // 10元 × 1000股 = 10000元，占 100000 总资金 = 10% == 上限，通过
        var check = await svc.CheckSingleTradeAsync(MakeSignal(10m, 1000), totalCapital: 100_000m);

        Assert.True(check.Passed);
    }

    [Fact]
    public async Task CheckSingleTrade_ExceedsLimit_Fails()
    {
        var svc = CreateService();
        // 10元 × 2000股 = 20000元，占 100000 = 20% > 10%，拒绝
        var check = await svc.CheckSingleTradeAsync(MakeSignal(10m, 2000), totalCapital: 100_000m);

        Assert.False(check.Passed);
        Assert.Equal("单笔交易限制", check.Name);
    }

    // ===== 总仓位限制 =====

    [Fact]
    public async Task CheckTotalPosition_WithinLimit_Passes()
    {
        var svc = CreateService();
        var positions = new List<PortfolioPosition>
        {
            new() { MarketValue = 70_000m }
        };

        var check = await svc.CheckTotalPositionAsync(positions, totalCapital: 100_000m);

        Assert.True(check.Passed);
    }

    [Fact]
    public async Task CheckTotalPosition_ExceedsLimit_Fails()
    {
        var svc = CreateService();
        var positions = new List<PortfolioPosition>
        {
            new() { MarketValue = 50_000m },
            new() { MarketValue = 40_000m } // 合计 90% > 80%
        };

        var check = await svc.CheckTotalPositionAsync(positions, totalCapital: 100_000m);

        Assert.False(check.Passed);
    }

    [Fact]
    public async Task CheckTotalPosition_EmptyPortfolio_Passes()
    {
        var svc = CreateService();

        var check = await svc.CheckTotalPositionAsync(EmptyPositions(), totalCapital: 100_000m);

        Assert.True(check.Passed);
    }

    // ===== 单票仓位限制 =====

    [Fact]
    public async Task CheckSingleStock_WithinLimit_Passes()
    {
        var svc = CreateService();
        // 15000 / 100000 = 15% < 20%
        var check = await svc.CheckSingleStockPositionAsync("000001", 15_000m, 100_000m);

        Assert.True(check.Passed);
    }

    [Fact]
    public async Task CheckSingleStock_ExceedsLimit_Fails()
    {
        var svc = CreateService();
        // 25000 / 100000 = 25% > 20%
        var check = await svc.CheckSingleStockPositionAsync("000001", 25_000m, 100_000m);

        Assert.False(check.Passed);
    }

    // ===== 板块集中度 =====

    [Fact]
    public async Task CheckSectorConcentration_WithinLimit_Passes()
    {
        var svc = CreateService();
        var positions = new List<PortfolioPosition>
        {
            new() { Code = "000001", Name = "平安银行", Industry = "银行", MarketValue = 30_000m },
            new() { Code = "601318", Name = "中国平安", Industry = "保险", MarketValue = 30_000m }
        };

        var check = await svc.CheckSectorConcentrationAsync(positions, totalCapital: 100_000m);

        Assert.True(check.Passed);
    }

    [Fact]
    public async Task CheckSectorConcentration_ExceedsLimit_Fails()
    {
        var svc = CreateService();
        var positions = new List<PortfolioPosition>
        {
            new() { Code = "000001", Name = "平安银行", Industry = "银行", MarketValue = 30_000m },
            new() { Code = "600036", Name = "招商银行", Industry = "银行", MarketValue = 20_000m }
            // 银行板块合计 50% > 40%
        };

        var check = await svc.CheckSectorConcentrationAsync(positions, totalCapital: 100_000m);

        Assert.False(check.Passed);
    }

    // ===== 综合风控检查 =====

    [Fact]
    public async Task CheckRisk_AllPassingSignal_ReturnsPassed()
    {
        var svc = CreateService();
        // 信号: 10元 × 500股 = 5000元，占 100000 = 5%（低于各项限制）
        var signal = MakeSignal(10m, 500);
        var positions = new List<PortfolioPosition> { new() { MarketValue = 20_000m } };

        var result = await svc.CheckRiskAsync(signal, positions, totalCapital: 100_000m);

        Assert.True(result.Passed);
        Assert.Equal(RiskLevel.Low, result.RiskLevel);
    }

    [Fact]
    public async Task CheckRisk_OneFailingCheck_ReturnsMediumRisk()
    {
        var svc = CreateService();
        // 单笔 20% > 10% 限制，触发一项失败 → Medium
        var signal = MakeSignal(10m, 2000);
        var positions = EmptyPositions();

        var result = await svc.CheckRiskAsync(signal, positions, totalCapital: 100_000m);

        Assert.False(result.Passed);
        Assert.Equal(RiskLevel.Medium, result.RiskLevel);
    }

    [Fact]
    public async Task CheckRisk_TwoFailingChecks_ReturnsHighRisk()
    {
        var svc = CreateService();
        // 单笔20%、总仓位85%，两项失败 → High
        var signal = MakeSignal(10m, 2000);
        var positions = new List<PortfolioPosition>
        {
            new() { MarketValue = 85_000m }
        };

        var result = await svc.CheckRiskAsync(signal, positions, totalCapital: 100_000m);

        Assert.False(result.Passed);
        Assert.True(result.RiskLevel >= RiskLevel.High);
    }

    [Fact]
    public async Task CheckRisk_HasFourChecks()
    {
        var svc = CreateService();
        var result = await svc.CheckRiskAsync(MakeSignal(), EmptyPositions(), 100_000m);

        Assert.Equal(4, result.Checks.Count);
    }

    [Fact]
    public async Task CheckRisk_FailedResult_HasSuggestion()
    {
        var svc = CreateService();
        var signal = MakeSignal(10m, 2000); // 单笔超限

        var result = await svc.CheckRiskAsync(signal, EmptyPositions(), 100_000m);

        Assert.False(string.IsNullOrEmpty(result.Suggestion));
    }

    [Fact]
    public async Task CheckRisk_PassedResult_SuggestionMentionsOk()
    {
        var svc = CreateService();
        var signal = MakeSignal(10m, 100); // 10元 × 100股 = 0.1% 各项均过

        var result = await svc.CheckRiskAsync(signal, EmptyPositions(), 100_000m);

        Assert.True(result.Passed);
        Assert.Contains("通过", result.Suggestion);
    }
}
