using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Knowledge.Services;

/// <summary>
/// 产业链图谱服务
/// </summary>
public class IndustryChainGraphService : IIndustryChainGraph
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILogger<IndustryChainGraphService> _logger;

    public IndustryChainGraphService(AIStockDbContext dbContext, ILogger<IndustryChainGraphService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<long> AddChainNodeAsync(IndustryChainNode node)
    {
        var entity = new IndustryChainEntity
        {
            ChainName = node.ChainName,
            ParentNode = node.ParentNode,
            ChildNode = node.ChildNode,
            Level = node.Level,
            NodeType = node.NodeType,
            Description = node.Description
        };

        _dbContext.IndustryChain.Add(entity);
        await _dbContext.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<long> AddCompanyChainRelationAsync(CompanyChainRelation relation)
    {
        var entity = new CompanyChainRelationEntity
        {
            CompanyCode = relation.CompanyCode,
            ChainId = relation.ChainId,
            ChainNode = relation.ChainNode,
            Role = relation.Role
        };

        _dbContext.CompanyChainRelation.Add(entity);
        await _dbContext.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<List<IndustryChainNode>> GetChainStructureAsync(string chainName)
    {
        var entities = await _dbContext.IndustryChain
            .Where(e => e.ChainName == chainName)
            .OrderBy(e => e.Level)
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyChainRelation>> GetCompanyChainPositionsAsync(string companyCode)
    {
        var entities = await _dbContext.CompanyChainRelation
            .Where(e => e.CompanyCode == companyCode)
            .ToListAsync();

        var result = new List<CompanyChainRelation>();
        foreach (var entity in entities)
        {
            var model = MapToModel(entity);
            var chain = await _dbContext.IndustryChain.FindAsync(entity.ChainId);
            if (chain != null)
            {
                model.ChainName = chain.ChainName;
            }
            result.Add(model);
        }

        return result;
    }

    public async Task<List<CompanyChainRelation>> GetChainCompaniesAsync(string chainName, string? role = null)
    {
        var chainIds = await _dbContext.IndustryChain
            .Where(e => e.ChainName == chainName)
            .Select(e => e.Id)
            .ToListAsync();

        var query = _dbContext.CompanyChainRelation
            .Where(e => chainIds.Contains(e.ChainId));

        if (!string.IsNullOrEmpty(role))
        {
            query = query.Where(e => e.Role == role);
        }

        var entities = await query.ToListAsync();
        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<string>> GetAllChainNamesAsync()
    {
        return await _dbContext.IndustryChain
            .Select(e => e.ChainName)
            .Distinct()
            .ToListAsync();
    }

    public async Task<ConceptDiffusionResult> DiffuseConceptAsync(string coreEvent, List<string> relatedConcepts)
    {
        var result = new ConceptDiffusionResult
        {
            CoreEvent = coreEvent
        };

        // 查找关联产业链（先获取所有，再在内存中过滤）
        var allChains = await _dbContext.IndustryChain.ToListAsync();
        var chains = allChains
            .Where(e => relatedConcepts.Any(c => e.ChainName.Contains(c) || (e.Description != null && e.Description.Contains(c))))
            .Select(e => e.ChainName)
            .Distinct()
            .ToList();

        result.RelatedChains = chains;

        // 查找产业链中的公司
        foreach (var chainName in chains)
        {
            var chainIds = allChains
                .Where(e => e.ChainName == chainName)
                .Select(e => e.Id)
                .ToList();

            var companies = await _dbContext.CompanyChainRelation
                .Where(e => chainIds.Contains(e.ChainId))
                .ToListAsync();

            foreach (var company in companies)
            {
                if (!result.BenefitCompanies.Any(b => b.Code == company.CompanyCode))
                {
                    var stockInfo = await _dbContext.StockBase.FindAsync(company.CompanyCode);
                    result.BenefitCompanies.Add(new BenefitCompany
                    {
                        Code = company.CompanyCode,
                        Name = stockInfo?.Name ?? company.CompanyCode,
                        Reason = $"位于{chainName}产业链的{company.Role}环节",
                        BenefitLevel = CalculateBenefitLevel(company.Role),
                        ChainRole = company.Role
                    });
                }
            }
        }

        // 按受益程度排序
        result.BenefitCompanies = result.BenefitCompanies
            .OrderByDescending(b => b.BenefitLevel)
            .Take(20)
            .ToList();

        return result;
    }

    private static int CalculateBenefitLevel(string? role)
    {
        return role switch
        {
            "upstream" => 8,    // 上游受益最大
            "midstream" => 6,   // 中游次之
            "downstream" => 4,  // 下游再次
            _ => 5
        };
    }

    private static IndustryChainNode MapToModel(IndustryChainEntity entity)
    {
        return new IndustryChainNode
        {
            Id = entity.Id,
            ChainName = entity.ChainName,
            ParentNode = entity.ParentNode,
            ChildNode = entity.ChildNode,
            Level = entity.Level,
            NodeType = entity.NodeType,
            Description = entity.Description
        };
    }

    private static CompanyChainRelation MapToModel(CompanyChainRelationEntity entity)
    {
        return new CompanyChainRelation
        {
            Id = entity.Id,
            CompanyCode = entity.CompanyCode,
            ChainId = entity.ChainId,
            ChainNode = entity.ChainNode,
            Role = entity.Role
        };
    }
}
