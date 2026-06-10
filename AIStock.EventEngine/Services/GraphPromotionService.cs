using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.EventEngine.Services;

/// <summary>
/// 候选边晋升服务 — 将达到阈值（多次印证 + 高可信度）的候选边晋升到权威图谱：
/// concept → stock_concept_relation；co-occur → company_relation。
/// </summary>
public class GraphPromotionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkerConfigProvider _config;
    private GraphPromotionOptions _options = new();
    private readonly ILogger<GraphPromotionService> _logger;

    public GraphPromotionService(
        IServiceScopeFactory scopeFactory,
        IWorkerConfigProvider config,
        ILogger<GraphPromotionService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<int> PromoteAsync(CancellationToken ct = default)
    {
        _options = await _config.GetAsync<GraphPromotionOptions>(GraphPromotionOptions.SectionName, ct);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var candidates = await db.GraphCandidateEdge
            .Where(e => !e.Promoted
                        && e.MentionCount >= _options.MinMentions
                        && e.Credibility >= _options.MinCredibility)
            .OrderByDescending(e => e.MentionCount)
            .Take(_options.MaxPerRun)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("无达到晋升阈值的候选边");
            return 0;
        }

        // 预解析公司名→代码（concept 边的 FromEntity 可能是公司名）
        var names = candidates.Where(c => c.EdgeType == "concept").Select(c => c.FromEntity)
            .Union(candidates.Where(c => c.EdgeType == "co-occur").SelectMany(c => new[] { c.FromEntity, c.ToEntity }))
            .Distinct().ToList();
        var codeByName = await db.StockBase
            .Where(s => names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, s => s.Code, ct);

        var promoted = 0;
        foreach (var edge in candidates)
        {
            try
            {
                if (edge.EdgeType == "concept")
                {
                    var code = ResolveCode(edge.FromEntity, codeByName);
                    if (code == null) continue; // 解析不出代码，留候选
                    var exists = await db.StockConceptRelation
                        .AnyAsync(r => r.StockCode == code && r.ConceptName == edge.ToEntity, ct);
                    if (!exists)
                        db.StockConceptRelation.Add(new StockConceptRelationEntity
                        {
                            StockCode = code,
                            ConceptName = edge.ToEntity,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        });
                }
                else if (edge.EdgeType == "co-occur")
                {
                    var exists = await db.CompanyRelation.AnyAsync(r =>
                        r.SourceCompany == edge.FromEntity && r.TargetCompany == edge.ToEntity && r.RelationType == "related", ct);
                    if (!exists)
                        db.CompanyRelation.Add(new CompanyRelationEntity
                        {
                            SourceCompany = edge.FromEntity,
                            TargetCompany = edge.ToEntity,
                            RelationType = "related",
                            Weight = Math.Min(1.0m, edge.MentionCount / 10m),
                            Description = $"小作文共现晋升（提及{edge.MentionCount}次，可信度{edge.Credibility}）",
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        });
                }
                else
                {
                    continue;
                }

                edge.Promoted = true;
                edge.UpdatedAt = DateTime.Now;
                promoted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "候选边晋升失败 {From}->{To}({Type})", edge.FromEntity, edge.ToEntity, edge.EdgeType);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("候选边晋升完成：{Promoted}/{Total}", promoted, candidates.Count);
        return promoted;
    }

    /// <summary>FromEntity 已是6位代码则直接用，否则按公司名解析</summary>
    private static string? ResolveCode(string entity, Dictionary<string, string> codeByName)
    {
        if (entity.Length == 6 && entity.All(char.IsDigit)) return entity;
        return codeByName.TryGetValue(entity, out var code) ? code : null;
    }
}
