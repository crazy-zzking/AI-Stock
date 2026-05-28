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

    public async Task<long> AddChainNodeAsync(IndustryChainNode node, CancellationToken cancellationToken = default)
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
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<long> AddCompanyChainRelationAsync(CompanyChainRelation relation, CancellationToken cancellationToken = default)
    {
        var entity = new CompanyChainRelationEntity
        {
            CompanyCode = relation.CompanyCode,
            ChainId = relation.ChainId,
            ChainNode = relation.ChainNode,
            Role = relation.Role
        };

        _dbContext.CompanyChainRelation.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<List<IndustryChainNode>> GetChainStructureAsync(string chainName, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.IndustryChain
            .Where(e => e.ChainName == chainName)
            .OrderBy(e => e.Level)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyChainRelation>> GetCompanyChainPositionsAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CompanyChainRelation
            .Where(e => e.CompanyCode == companyCode)
            .ToListAsync(cancellationToken);

        var chainIds = entities.Select(e => e.ChainId).Distinct().ToList();
        var chains = await _dbContext.IndustryChain
            .Where(c => chainIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.ChainName, cancellationToken);

        var result = new List<CompanyChainRelation>();
        foreach (var entity in entities)
        {
            var model = MapToModel(entity);
            if (chains.TryGetValue(entity.ChainId, out var chainName))
            {
                model.ChainName = chainName;
            }
            result.Add(model);
        }

        return result;
    }

    public async Task<List<CompanyChainRelation>> GetChainCompaniesAsync(string chainName, string? role = null, CancellationToken cancellationToken = default)
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

        var entities = await query.ToListAsync(cancellationToken);
        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<string>> GetAllChainNamesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.IndustryChain
            .Select(e => e.ChainName)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<ConceptDiffusionResult> DiffuseConceptAsync(string coreEvent, List<string> relatedConcepts, CancellationToken cancellationToken = default)
    {
        var result = new ConceptDiffusionResult
        {
            CoreEvent = coreEvent
        };

        var chains = await _dbContext.IndustryChain
            .Where(e => relatedConcepts.Any(c => e.ChainName.Contains(c) || (e.Description != null && e.Description.Contains(c))))
            .Select(e => e.ChainName)
            .Distinct()
            .ToListAsync(cancellationToken);

        result.RelatedChains = chains;

        var chainNames = chains.ToList();
        var allChainEntities = await _dbContext.IndustryChain
            .Where(e => chainNames.Contains(e.ChainName))
            .ToListAsync(cancellationToken);

        var allChainIds = allChainEntities.Select(e => e.Id).ToList();
        var allCompanies = await _dbContext.CompanyChainRelation
            .Where(e => allChainIds.Contains(e.ChainId))
            .ToListAsync(cancellationToken);

        var companyCodes = allCompanies.Select(c => c.CompanyCode).Distinct().ToList();
        var stockInfos = await _dbContext.StockBase
            .Where(s => companyCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, cancellationToken);

        foreach (var company in allCompanies)
        {
            if (!result.BenefitCompanies.Any(b => b.Code == company.CompanyCode))
            {
                var chainName = allChainEntities.FirstOrDefault(c => c.Id == company.ChainId)?.ChainName ?? "";
                stockInfos.TryGetValue(company.CompanyCode, out var stockName);

                result.BenefitCompanies.Add(new BenefitCompany
                {
                    Code = company.CompanyCode,
                    Name = stockName ?? company.CompanyCode,
                    Reason = $"位于{chainName}产业链的{company.Role}环节",
                    BenefitLevel = CalculateBenefitLevel(company.Role),
                    ChainRole = company.Role
                });
            }
        }

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
