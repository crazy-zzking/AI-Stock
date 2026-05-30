using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 持仓管理实现 - 通过IDataProvider接口解耦
/// </summary>
public class PositionManagerService : IPositionManager
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PositionManagerService> _logger;

    public PositionManagerService(
        IDataProviderResolver dataProviderResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<PositionManagerService> logger)
    {
        _dataProviderResolver = dataProviderResolver;
        _scopeFactory = scopeFactory;
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

            // 从 stock_base 批量补充行业信息（券商持仓不含行业，板块集中度风控依赖此字段）
            var industryMap = await GetIndustryMapAsync(accountInfo.Positions.Select(p => p.Code).ToList());

            var portfolioPositions = accountInfo.Positions.Select(p => new PortfolioPosition
            {
                Code = p.Code,
                Name = p.Name,
                Volume = p.Volume,
                CostPrice = p.CostPrice,
                CurrentPrice = p.CurrentPrice,
                MarketValue = p.MarketValue,
                Profit = p.Profit,
                ProfitRate = p.ProfitRate,
                Industry = industryMap.GetValueOrDefault(p.Code) ?? string.Empty
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

    /// <summary>
    /// 从 stock_base 批量查询代码 → 行业映射。查询失败返回空表，不影响持仓返回。
    /// </summary>
    private async Task<Dictionary<string, string?>> GetIndustryMapAsync(List<string> codes)
    {
        if (codes.Count == 0)
            return new Dictionary<string, string?>();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

            return await dbContext.StockBase
                .Where(s => codes.Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, s => s.Industry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load industry map for positions");
            return new Dictionary<string, string?>();
        }
    }

    public async Task<PortfolioPosition?> GetPositionAsync(string code)
    {
        var summary = await GetPositionSummaryAsync();
        return summary.Positions.FirstOrDefault(p => p.Code == code);
    }
}
