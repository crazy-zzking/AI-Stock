using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Knowledge.Services;

/// <summary>
/// 标的筛选器服务
/// </summary>
public class StockFilterService : IStockFilter
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILogger<StockFilterService> _logger;

    public StockFilterService(AIStockDbContext dbContext, ILogger<StockFilterService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<StockFilterResult>> FilterStocksAsync(StockFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StockBase.AsQueryable();

        query = query.Where(s => !s.IsDelisted);

        if (criteria.RelatedConcepts != null && criteria.RelatedConcepts.Any())
        {
            var conceptStocks = await GetStocksByConcepts(criteria.RelatedConcepts, cancellationToken);
            var conceptCodes = conceptStocks.Select(s => s.Code).ToList();
            query = query.Where(s => conceptCodes.Contains(s.Code));
        }

        if (criteria.RelatedIndustries != null && criteria.RelatedIndustries.Any())
        {
            query = query.Where(s => criteria.RelatedIndustries.Contains(s.Industry!));
        }

        var stocks = await query.ToListAsync(cancellationToken);
        var stockCodes = stocks.Select(s => s.Code).ToList();

        var latestKlines = await _dbContext.KlineData
            .Where(k => stockCodes.Contains(k.Code) && k.Interval == "daily")
            .GroupBy(k => k.Code)
            .Select(g => new { Code = g.Key, Latest = g.OrderByDescending(k => k.DateTime).FirstOrDefault() })
            .ToDictionaryAsync(x => x.Code, x => x.Latest, cancellationToken);

        var results = new List<StockFilterResult>();

        foreach (var stock in stocks)
        {
            var result = new StockFilterResult
            {
                Code = stock.Code,
                Name = stock.Name
            };

            if (latestKlines.TryGetValue(stock.Code, out var latestKline) && latestKline != null)
            {
                result.RisePercent = latestKline.ChangePercent ?? 0;

                if (criteria.ExcludeStarted && criteria.MaxRisePercent.HasValue && result.RisePercent > criteria.MaxRisePercent.Value)
                {
                    continue;
                }
            }

            result.Score = CalculateFilterScore(result, criteria);
            result.Reason = GenerateFilterReason(result, criteria);

            results.Add(result);
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(criteria.Count)
            .ToList();
    }

    public async Task<List<StockFilterResult>> FilterByConceptsAsync(List<string> concepts, int count = 20, CancellationToken cancellationToken = default)
    {
        var criteria = new StockFilterCriteria
        {
            RelatedConcepts = concepts,
            Count = count,
            ExcludeStarted = true,
            MaxRisePercent = 5
        };

        return await FilterStocksAsync(criteria, cancellationToken);
    }

    public async Task<List<StockFilterResult>> FilterByChainAsync(string chainName, string? role = null, int count = 20, CancellationToken cancellationToken = default)
    {
        var chainIds = await _dbContext.IndustryChain
            .Where(e => e.ChainName == chainName)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var query = _dbContext.CompanyChainRelation
            .Where(e => chainIds.Contains(e.ChainId));

        if (!string.IsNullOrEmpty(role))
        {
            query = query.Where(e => e.Role == role);
        }

        var companies = await query.ToListAsync(cancellationToken);
        var companyCodes = companies.Select(c => c.CompanyCode).Distinct().ToList();

        var stocks = await _dbContext.StockBase
            .Where(s => companyCodes.Contains(s.Code) && !s.IsDelisted)
            .ToDictionaryAsync(s => s.Code, s => s, cancellationToken);

        var latestKlines = await _dbContext.KlineData
            .Where(k => companyCodes.Contains(k.Code) && k.Interval == "daily")
            .GroupBy(k => k.Code)
            .Select(g => new { Code = g.Key, Latest = g.OrderByDescending(k => k.DateTime).FirstOrDefault() })
            .ToDictionaryAsync(x => x.Code, x => x.Latest, cancellationToken);

        var results = new List<StockFilterResult>();

        foreach (var company in companies)
        {
            if (!stocks.TryGetValue(company.CompanyCode, out var stock)) continue;

            latestKlines.TryGetValue(company.CompanyCode, out var latestKline);

            var result = new StockFilterResult
            {
                Code = company.CompanyCode,
                Name = stock.Name,
                RisePercent = latestKline?.ChangePercent ?? 0,
                Score = CalculateChainBenefitScore(company.Role),
                Reason = $"位于{chainName}产业链的{company.Role}环节"
            };

            results.Add(result);
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(count)
            .ToList();
    }

    private async Task<List<StockBase>> GetStocksByConcepts(List<string> concepts, CancellationToken cancellationToken = default)
    {
        var chainIds = await _dbContext.IndustryChain
            .Where(e => concepts.Any(c => e.ChainName.Contains(c) || e.Description!.Contains(c)))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var chainCompanies = await _dbContext.CompanyChainRelation
            .Where(e => chainIds.Contains(e.ChainId))
            .Select(e => e.CompanyCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        var relatedCompanies = await _dbContext.CompanyRelation
            .Where(e => concepts.Any(c => e.Description!.Contains(c)))
            .Select(e => e.TargetCompany)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allCodes = chainCompanies.Union(relatedCompanies).Distinct().ToList();

        return await _dbContext.StockBase
            .Where(s => allCodes.Contains(s.Code))
            .Select(s => new StockBase
            {
                Code = s.Code,
                Name = s.Name,
                Market = s.Market,
                Industry = s.Industry,
                IsDelisted = s.IsDelisted
            })
            .ToListAsync(cancellationToken);
    }

    private static decimal CalculateFilterScore(StockFilterResult result, StockFilterCriteria criteria)
    {
        var score = 5m; // 基础分

        // 小市值加分
        if (result.MarketCap > 0 && result.MarketCap < 50)
        {
            score += 2;
        }
        else if (result.MarketCap >= 50 && result.MarketCap < 100)
        {
            score += 1;
        }

        // 未启动加分
        if (result.RisePercent < 3)
        {
            score += 2;
        }
        else if (result.RisePercent < 5)
        {
            score += 1;
        }

        // 概念关联度加分
        if (result.RelatedConcepts.Count > 2)
        {
            score += 1;
        }

        return score;
    }

    private static decimal CalculateChainBenefitScore(string? role)
    {
        return role switch
        {
            "upstream" => 8,
            "midstream" => 6,
            "downstream" => 4,
            _ => 5
        };
    }

    private static string GenerateFilterReason(StockFilterResult result, StockFilterCriteria criteria)
    {
        var reasons = new List<string>();

        if (result.MarketCap > 0 && result.MarketCap < 50)
        {
            reasons.Add("小市值");
        }

        if (result.RisePercent < 3)
        {
            reasons.Add("未启动");
        }

        if (result.RelatedConcepts.Any())
        {
            reasons.Add($"关联概念：{string.Join(",", result.RelatedConcepts.Take(3))}");
        }

        return reasons.Any() ? string.Join("，" , reasons) : "符合筛选条件";
    }
}

/// <summary>
/// 股票基础信息（扩展）
/// </summary>
public class StockBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public bool IsDelisted { get; set; }
}
