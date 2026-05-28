using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 持仓管理实现 - 对接散户量化
/// </summary>
public class PositionManagerService : IPositionManager
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<PositionManagerService> _logger;

    public PositionManagerService(
        IDataProviderResolver dataProviderResolver,
        ILogger<PositionManagerService> logger)
    {
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    public async Task<PositionSummary> GetPositionSummaryAsync()
    {
        try
        {
            var provider = _dataProviderResolver.GetAllProviders()
                .FirstOrDefault(p => p.ProviderId == "sanhu");
            
            if (provider == null)
            {
                _logger.LogWarning("Sanhu provider not found");
                return new PositionSummary();
            }

            _logger.LogInformation("Calling GetAccountPositionsAsync on Sanhu provider");
            var positions = await provider.GetAccountPositionsAsync();
            _logger.LogInformation("Got {Count} positions from Sanhu", positions.Count);

            var portfolioPositions = positions.Select(p => new PortfolioPosition
            {
                Code = p.Code,
                Name = p.Name,
                Volume = p.Volume,
                CostPrice = p.CostPrice,
                CurrentPrice = p.CurrentPrice,
                MarketValue = p.MarketValue,
                Profit = p.Profit,
                ProfitRate = p.ProfitRate
            }).ToList();

            var totalProfit = portfolioPositions.Sum(p => p.Profit);
            var totalMarketValue = portfolioPositions.Sum(p => p.MarketValue);

            return new PositionSummary
            {
                TotalAssets = totalMarketValue,
                PositionValue = totalMarketValue,
                TotalProfit = totalProfit,
                TotalProfitRate = totalMarketValue > 0 ? totalProfit / totalMarketValue * 100 : 0,
                PositionCount = portfolioPositions.Count,
                ProfitCount = portfolioPositions.Count(p => p.Profit > 0),
                LossCount = portfolioPositions.Count(p => p.Profit < 0),
                Positions = portfolioPositions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get position summary");
            return new PositionSummary();
        }
    }

    public async Task<PortfolioPosition?> GetPositionAsync(string code)
    {
        var summary = await GetPositionSummaryAsync();
        return summary.Positions.FirstOrDefault(p => p.Code == code);
    }
}
