using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Knowledge.Services;

/// <summary>
/// 公司关系图谱服务
/// </summary>
public class CompanyRelationGraphService : ICompanyRelationGraph
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILogger<CompanyRelationGraphService> _logger;

    public CompanyRelationGraphService(AIStockDbContext dbContext, ILogger<CompanyRelationGraphService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<long> AddRelationAsync(CompanyRelation relation, CancellationToken cancellationToken = default)
    {
        var entity = new CompanyRelationEntity
        {
            SourceCompany = relation.SourceCompany,
            TargetCompany = relation.TargetCompany,
            RelationType = relation.RelationType,
            Weight = relation.Weight,
            Description = relation.Description
        };

        _dbContext.CompanyRelation.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<int> AddRelationsAsync(IEnumerable<CompanyRelation> relations, CancellationToken cancellationToken = default)
    {
        var entities = relations.Select(r => new CompanyRelationEntity
        {
            SourceCompany = r.SourceCompany,
            TargetCompany = r.TargetCompany,
            RelationType = r.RelationType,
            Weight = r.Weight,
            Description = r.Description
        }).ToList();

        _dbContext.CompanyRelation.AddRange(entities);
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<CompanyRelation>> GetCompanyRelationsAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => e.SourceCompany == companyCode || e.TargetCompany == companyCode)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetSuppliersAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => e.TargetCompany == companyCode && e.RelationType == "supplier")
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetCustomersAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => e.SourceCompany == companyCode && e.RelationType == "customer")
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetInvestmentsAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => (e.SourceCompany == companyCode || e.TargetCompany == companyCode) && e.RelationType == "invest")
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetControllingAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => (e.SourceCompany == companyCode || e.TargetCompany == companyCode) && e.RelationType == "control")
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<List<CompanyRelation>>> FindRelationPathAsync(string fromCode, string toCode, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        // 预加载全量邻接表到内存，避免DFS递归中的N+1查询
        var allRelations = await _dbContext.CompanyRelation.ToListAsync(cancellationToken);
        var adjacencyList = BuildAdjacencyList(allRelations);

        var paths = new List<List<CompanyRelation>>();
        var visited = new HashSet<string>();
        var currentPath = new List<CompanyRelation>();

        FindPathsInMemory(fromCode, toCode, maxDepth, visited, currentPath, paths, adjacencyList);

        return paths;
    }

    private static Dictionary<string, List<CompanyRelationEntity>> BuildAdjacencyList(List<CompanyRelationEntity> allRelations)
    {
        var adjacency = new Dictionary<string, List<CompanyRelationEntity>>();
        foreach (var relation in allRelations)
        {
            if (!adjacency.ContainsKey(relation.SourceCompany))
                adjacency[relation.SourceCompany] = new List<CompanyRelationEntity>();
            if (!adjacency.ContainsKey(relation.TargetCompany))
                adjacency[relation.TargetCompany] = new List<CompanyRelationEntity>();

            adjacency[relation.SourceCompany].Add(relation);
            adjacency[relation.TargetCompany].Add(relation);
        }
        return adjacency;
    }

    private void FindPathsInMemory(string current, string target, int depth, HashSet<string> visited, List<CompanyRelation> currentPath, List<List<CompanyRelation>> paths, Dictionary<string, List<CompanyRelationEntity>> adjacency)
    {
        if (depth <= 0) return;
        if (current == target)
        {
            paths.Add(new List<CompanyRelation>(currentPath));
            return;
        }

        visited.Add(current);

        if (adjacency.TryGetValue(current, out var relations))
        {
            foreach (var relation in relations)
            {
                var nextCompany = relation.SourceCompany == current ? relation.TargetCompany : relation.SourceCompany;
                if (!visited.Contains(nextCompany))
                {
                    currentPath.Add(MapToModel(relation));
                    FindPathsInMemory(nextCompany, target, depth - 1, visited, currentPath, paths, adjacency);
                    currentPath.RemoveAt(currentPath.Count - 1);
                }
            }
        }

        visited.Remove(current);
    }

    public async Task<bool> DeleteRelationAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CompanyRelation.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null) return false;

        _dbContext.CompanyRelation.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> AddRelationsAsync(IEnumerable<CompanyRelation> relations)
    {
        var entities = relations.Select(r => new CompanyRelationEntity
        {
            SourceCompany = r.SourceCompany,
            TargetCompany = r.TargetCompany,
            RelationType = r.RelationType,
            Weight = r.Weight,
            Description = r.Description
        }).ToList();

        _dbContext.CompanyRelation.AddRange(entities);
        return await _dbContext.SaveChangesAsync();
    }

    public async Task<List<CompanyRelation>> GetCompanyRelationsAsync(string companyCode)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => e.SourceCompany == companyCode || e.TargetCompany == companyCode)
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetSuppliersAsync(string companyCode)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => e.TargetCompany == companyCode && e.RelationType == "supplier")
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetCustomersAsync(string companyCode)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => e.SourceCompany == companyCode && e.RelationType == "customer")
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetInvestmentsAsync(string companyCode)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => (e.SourceCompany == companyCode || e.TargetCompany == companyCode) && e.RelationType == "invest")
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<CompanyRelation>> GetControllingAsync(string companyCode)
    {
        var entities = await _dbContext.CompanyRelation
            .Where(e => (e.SourceCompany == companyCode || e.TargetCompany == companyCode) && e.RelationType == "control")
            .ToListAsync();

        return entities.Select(MapToModel).ToList();
    }

    public async Task<List<List<CompanyRelation>>> FindRelationPathAsync(string fromCode, string toCode, int maxDepth = 3)
    {
        return await FindRelationPathAsync(fromCode, toCode, maxDepth, CancellationToken.None);
    }

    public async Task<bool> DeleteRelationAsync(long id)
    {
        var entity = await _dbContext.CompanyRelation.FindAsync(id);
        if (entity == null) return false;

        _dbContext.CompanyRelation.Remove(entity);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    private static CompanyRelation MapToModel(CompanyRelationEntity entity)
    {
        return new CompanyRelation
        {
            Id = entity.Id,
            SourceCompany = entity.SourceCompany,
            TargetCompany = entity.TargetCompany,
            RelationType = entity.RelationType,
            Weight = entity.Weight,
            Description = entity.Description
        };
    }
}
