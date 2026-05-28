using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Risk.Services;

/// <summary>
/// 风控引擎实现
/// </summary>
public class RiskEngineService : IRiskEngine
{
    private readonly ILogger<RiskEngineService> _logger;

    private const decimal MaxSingleStockPercent = 20;
    private const decimal MaxTotalPositionPercent = 80;
    private const decimal MaxSectorPercent = 40;
    private const decimal MaxSingleTradePercent = 10;

    public RiskEngineService(ILogger<RiskEngineService> logger)
    {
        _logger = logger;
    }

    public async Task<RiskCheckResult> CheckRiskAsync(TradeSignal signal, List<PortfolioPosition> positions, decimal totalCapital)
    {
        var result = new RiskCheckResult { Passed = true };

        var singleTradeCheck = await CheckSingleTradeAsync(signal, totalCapital);
        result.Checks.Add(singleTradeCheck);
        if (!singleTradeCheck.Passed) result.Passed = false;

        var totalPositionCheck = await CheckTotalPositionAsync(positions, totalCapital);
        result.Checks.Add(totalPositionCheck);
        if (!totalPositionCheck.Passed) result.Passed = false;

        var singleStockCheck = await CheckSingleStockPositionAsync(signal.Code, signal.Price * signal.Volume, totalCapital);
        result.Checks.Add(singleStockCheck);
        if (!singleStockCheck.Passed) result.Passed = false;

        var sectorCheck = await CheckSectorConcentrationAsync(positions, totalCapital);
        result.Checks.Add(sectorCheck);
        if (!sectorCheck.Passed) result.Passed = false;

        result.RiskLevel = CalculateRiskLevel(result.Checks);
        result.Suggestion = GenerateSuggestion(result);

        return result;
    }

    public async Task<RiskCheckItem> CheckSingleTradeAsync(TradeSignal signal, decimal totalCapital)
    {
        var tradeAmount = signal.Price * signal.Volume;
        var tradePercent = tradeAmount / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "单笔交易限制",
            Passed = tradePercent <= MaxSingleTradePercent,
            CurrentValue = tradePercent,
            LimitValue = MaxSingleTradePercent,
            Description = $"单笔交易占比: {tradePercent:F2}%，限制: {MaxSingleTradePercent}%"
        };
    }

    public async Task<RiskCheckItem> CheckTotalPositionAsync(List<PortfolioPosition> positions, decimal totalCapital)
    {
        var totalPositionValue = positions.Sum(p => p.MarketValue);
        var totalPercent = totalPositionValue / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "总仓位限制",
            Passed = totalPercent <= MaxTotalPositionPercent,
            CurrentValue = totalPercent,
            LimitValue = MaxTotalPositionPercent,
            Description = $"总仓位占比: {totalPercent:F2}%，限制: {MaxTotalPositionPercent}%"
        };
    }

    public async Task<RiskCheckItem> CheckSingleStockPositionAsync(string code, decimal positionValue, decimal totalCapital)
    {
        var positionPercent = positionValue / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "单票仓位限制",
            Passed = positionPercent <= MaxSingleStockPercent,
            CurrentValue = positionPercent,
            LimitValue = MaxSingleStockPercent,
            Description = $"单票仓位占比: {positionPercent:F2}%，限制: {MaxSingleStockPercent}%"
        };
    }

    public async Task<RiskCheckItem> CheckSectorConcentrationAsync(List<PortfolioPosition> positions, decimal totalCapital)
    {
        var sectorValue = positions.Sum(p => p.MarketValue);
        var sectorPercent = sectorValue / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "板块集中度限制",
            Passed = sectorPercent <= MaxSectorPercent,
            CurrentValue = sectorPercent,
            LimitValue = MaxSectorPercent,
            Description = $"板块集中度: {sectorPercent:F2}%，限制: {MaxSectorPercent}%"
        };
    }

    private string CalculateRiskLevel(List<RiskCheckItem> checks)
    {
        var failedCount = checks.Count(c => !c.Passed);

        if (failedCount >= 3) return "critical";
        if (failedCount >= 2) return "high";
        if (failedCount >= 1) return "medium";
        return "low";
    }

    private string GenerateSuggestion(RiskCheckResult result)
    {
        if (result.Passed)
        {
            return "风控检查通过，可以执行交易";
        }

        var suggestions = new List<string>();

        foreach (var check in result.Checks.Where(c => !c.Passed))
        {
            switch (check.Name)
            {
                case "单笔交易限制":
                    suggestions.Add("建议减小单笔交易金额");
                    break;
                case "总仓位限制":
                    suggestions.Add("建议降低总仓位");
                    break;
                case "单票仓位限制":
                    suggestions.Add("建议减小单票仓位");
                    break;
                case "板块集中度限制":
                    suggestions.Add("建议分散投资，降低板块集中度");
                    break;
            }
        }

        return string.Join("；", suggestions);
    }
}
