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

    public async Task<List<PortfolioPosition>> GetPositionsAsync()
    {
        try
        {
            var provider = _dataProviderResolver.GetDefaultProvider();
            var positions = await provider.GetAccountPositionsAsync();

            return positions.Select(p => new PortfolioPosition
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get positions");
            return new List<PortfolioPosition>();
        }
    }

    public async Task<PortfolioPosition?> GetPositionAsync(string code)
    {
        var positions = await GetPositionsAsync();
        return positions.FirstOrDefault(p => p.Code == code);
    }

    public Task UpdatePositionAsync(PortfolioPosition position)
    {
        _logger.LogWarning("UpdatePosition not supported for real trading");
        return Task.CompletedTask;
    }

    public Task RemovePositionAsync(string code)
    {
        _logger.LogWarning("RemovePosition not supported for real trading");
        return Task.CompletedTask;
    }

    public async Task<PositionSummary> GetSummaryAsync()
    {
        var positions = await GetPositionsAsync();
        var summary = new PositionSummary
        {
            TotalMarketValue = positions.Sum(p => p.MarketValue),
            TotalProfit = positions.Sum(p => p.Profit),
            PositionCount = positions.Count,
            ProfitCount = positions.Count(p => p.Profit > 0),
            LossCount = positions.Count(p => p.Profit < 0),
            Positions = positions
        };

        if (summary.TotalMarketValue > 0)
        {
            summary.TotalProfitRate = summary.TotalProfit / summary.TotalMarketValue * 100;
        }

        return summary;
    }
}
