using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 持仓管理实现 - 通过IDataProvider接口解耦
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
            var provider = _dataProviderResolver.GetPrimaryProvider(DataCapability.Trading);
            
            if (provider == null)
            {
                _logger.LogWarning("No trading provider available");
                return new PositionSummary();
            }

            var accountInfo = await provider.GetAccountInfoAsync();

            var portfolioPositions = accountInfo.Positions.Select(p => new PortfolioPosition
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

            return new PositionSummary
            {
                TotalAssets = accountInfo.TotalAssets,
                AvailableBalance = accountInfo.AvailableBalance,
                PositionValue = portfolioPositions.Sum(p => p.MarketValue),
                TotalProfit = accountInfo.TotalProfit,
                TotalProfitRate = accountInfo.TotalAssets > 0 ? accountInfo.TotalProfit / accountInfo.TotalAssets * 100 : 0,
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
