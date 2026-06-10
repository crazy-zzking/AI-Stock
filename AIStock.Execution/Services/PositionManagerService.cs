using System.Text.Json;
using System.Text.Json.Serialization;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AIStock.Execution.Services;

/// <summary>
/// 持仓管理实现 - 通过IDataProvider接口解耦
/// </summary>
public class PositionManagerService : IPositionManager
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<PositionManagerService> _logger;

    private const string CacheKey = "aistock:cache:positions";
    private const string UpdatedKey = "aistock:cache:positions:updated";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = AIStock.Core.Json.AppJson.CjkEncoder
    };

    public PositionManagerService(
        IDataProviderResolver dataProviderResolver,
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        ILogger<PositionManagerService> logger)
    {
        _dataProviderResolver = dataProviderResolver;
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
    }

    public async Task<PositionSummary> GetPositionSummaryAsync()
    {
        // 1. 优先从 Redis 缓存读取
        if (_redis != null)
        {
            var cached = await GetCachedAccountInfoAsync();
            if (cached != null)
            {
                var industryMap = await GetIndustryMapAsync(cached.Positions.Select(p => p.Code).ToList());
                var summary = BuildSummary(cached, industryMap);
                summary.UpdatedAt = await GetCachedUpdatedAtAsync();
                return summary;
            }
        }

        // 2. 缓存未命中，直连 Provider
        return await FetchFromProviderAsync();
    }

    /// <summary>
    /// 强制从 Provider 刷新并更新 Redis 缓存
    /// </summary>
    public async Task<PositionSummary> RefreshAsync()
    {
        var summary = await FetchFromProviderAsync();
        summary.UpdatedAt = DateTime.Now;

        if (_redis != null)
        {
            var provider = _dataProviderResolver.GetPrimaryProvider(DataCapability.Trading);
            if (provider != null)
            {
                var accountInfo = await provider.GetAccountInfoAsync();
                if (accountInfo.Positions.Count > 0 || accountInfo.TotalAssets > 0)
                {
                    var json = JsonSerializer.Serialize(accountInfo, JsonOptions);
                    var db = _redis.GetDatabase();
                    await db.StringSetAsync(CacheKey, json);
                    await db.StringSetAsync(UpdatedKey, DateTime.Now.ToString("O"));
                    _logger.LogInformation("Position cache refreshed via API");
                }
            }
        }

        return summary;
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

    /// <summary>
    /// 从 Redis 读取缓存更新时间
    /// </summary>
    private async Task<DateTime?> GetCachedUpdatedAtAsync()
    {
        try
        {
            if (_redis == null) return null;
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(UpdatedKey);
            if (value.IsNullOrEmpty) return null;
            return DateTime.Parse(value!);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 Redis 缓存读取 AccountInfo
    /// </summary>
    private async Task<AccountInfo?> GetCachedAccountInfoAsync()
    {
        try
        {
            if (_redis == null) return null;
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync(CacheKey);
            if (json.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<AccountInfo>(json!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read position cache from Redis");
            return null;
        }
    }

    /// <summary>
    /// 从 Provider 直连获取持仓（原有逻辑）
    /// </summary>
    private async Task<PositionSummary> FetchFromProviderAsync()
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
            var industryMap = await GetIndustryMapAsync(accountInfo.Positions.Select(p => p.Code).ToList());
            return BuildSummary(accountInfo, industryMap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get position summary");
            return new PositionSummary();
        }
    }

    /// <summary>
    /// 将 AccountInfo + 行业映射 构建为 PositionSummary
    /// </summary>
    private static PositionSummary BuildSummary(AccountInfo accountInfo, Dictionary<string, string?> industryMap)
    {
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
}
