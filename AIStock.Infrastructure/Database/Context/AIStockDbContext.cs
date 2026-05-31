using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Infrastructure.Database.Context;

/// <summary>
/// AIStock数据库上下文
/// </summary>
public class AIStockDbContext : DbContext
{
    public AIStockDbContext(DbContextOptions<AIStockDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 股票基础信息
    /// </summary>
    public DbSet<StockBaseEntity> StockBase { get; set; }

    /// <summary>
    /// K线数据
    /// </summary>
    public DbSet<KlineDataEntity> KlineData { get; set; }

    /// <summary>
    /// 公司关系
    /// </summary>
    public DbSet<CompanyRelationEntity> CompanyRelation { get; set; }

    /// <summary>
    /// 产业链
    /// </summary>
    public DbSet<IndustryChainEntity> IndustryChain { get; set; }

    /// <summary>
    /// 公司-产业链关联
    /// </summary>
    public DbSet<CompanyChainRelationEntity> CompanyChainRelation { get; set; }

    /// <summary>
    /// LLM模型配置
    /// </summary>
    public DbSet<LLMModelConfigEntity> LLMModelConfig { get; set; }

    /// <summary>
    /// 事件记录
    /// </summary>
    public DbSet<EventRecordEntity> EventRecord { get; set; }

    /// <summary>
    /// 交易记录
    /// </summary>
    public DbSet<TradeRecordEntity> TradeRecord { get; set; }

    /// <summary>
    /// 持仓记录
    /// </summary>
    public DbSet<PositionEntity> Position { get; set; }

    /// <summary>
    /// Prompt模板
    /// </summary>
    public DbSet<PromptTemplateEntity> PromptTemplate { get; set; }

    /// <summary>
    /// 事件-股票关联
    /// </summary>
    public DbSet<EventStockRelationEntity> EventStockRelation { get; set; }

    /// <summary>
    /// 事件-概念关联
    /// </summary>
    public DbSet<EventConceptRelationEntity> EventConceptRelation { get; set; }

    /// <summary>
    /// Agent记忆
    /// </summary>
    public DbSet<AgentMemoryEntity> AgentMemory { get; set; }

    /// <summary>
    /// 股票-概念关联
    /// </summary>
    public DbSet<StockConceptRelationEntity> StockConceptRelation { get; set; }

    /// <summary>
    /// 知识图谱候选边（小作文/情报推断，隔离）
    /// </summary>
    public DbSet<GraphCandidateEdgeEntity> GraphCandidateEdge { get; set; }

    /// <summary>
    /// 龙虎榜记录
    /// </summary>
    public DbSet<DragonTigerEntity> DragonTiger { get; set; }

    /// <summary>
    /// 每日市场快照（选股引擎数据源）
    /// </summary>
    public DbSet<DailyMarketSnapshotEntity> DailyMarketSnapshot { get; set; }

    /// <summary>
    /// 龙虎榜席位明细
    /// </summary>
    public DbSet<DragonTigerSeatEntity> DragonTigerSeat { get; set; }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity.GetType().GetProperty("CreatedAt") != null && entry.State == EntityState.Added)
            {
                entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
            }
            if (entry.Entity.GetType().GetProperty("UpdatedAt") != null)
            {
                entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 股票基础信息
        modelBuilder.Entity<StockBaseEntity>(entity =>
        {
            entity.HasKey(e => e.Code);
            entity.HasIndex(e => e.Market);
            entity.HasIndex(e => e.Industry);
        });

        // 股票-概念关联
        modelBuilder.Entity<StockConceptRelationEntity>(entity =>
        {
            entity.HasIndex(e => new { e.StockCode, e.ConceptName }).IsUnique();
            entity.HasIndex(e => e.ConceptName);
        });

        // 知识图谱候选边
        modelBuilder.Entity<GraphCandidateEdgeEntity>(entity =>
        {
            entity.HasIndex(e => new { e.FromEntity, e.ToEntity, e.EdgeType }).IsUnique();
            entity.HasIndex(e => e.EdgeType);
        });

        // K线数据
        modelBuilder.Entity<KlineDataEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Code, e.DateTime, e.Interval }).IsUnique();
            entity.HasIndex(e => e.DateTime);
        });

        // 公司关系
        modelBuilder.Entity<CompanyRelationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SourceCompany, e.TargetCompany, e.RelationType }).IsUnique();
            entity.HasIndex(e => e.SourceCompany);
            entity.HasIndex(e => e.TargetCompany);
        });

        // 产业链
        modelBuilder.Entity<IndustryChainEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChainName);
            entity.HasIndex(e => new { e.ChainName, e.ParentNode, e.ChildNode });
        });

        // 公司-产业链关联
        modelBuilder.Entity<CompanyChainRelationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CompanyCode, e.ChainId }).IsUnique();
            entity.HasIndex(e => e.CompanyCode);
        });

        // LLM模型配置
        modelBuilder.Entity<LLMModelConfigEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // 事件记录（content/title 等存情报文本，可能含 emoji，需 utf8mb4 才存得下 4 字节字符）
        modelBuilder.Entity<EventRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.EventTime);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasCharSet("utf8mb4");
        });

        // 交易记录
        modelBuilder.Entity<TradeRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        // 持仓记录
        modelBuilder.Entity<PositionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
        });

        // 事件-股票关联
        modelBuilder.Entity<EventStockRelationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EventId, e.StockCode }).IsUnique();
            entity.HasIndex(e => e.StockCode);
            entity.HasOne(e => e.Event)
                  .WithMany()
                  .HasForeignKey(e => e.EventId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // 事件-概念关联
        modelBuilder.Entity<EventConceptRelationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EventId, e.ConceptName }).IsUnique();
            entity.HasIndex(e => e.ConceptName);
            entity.HasOne(e => e.Event)
                  .WithMany()
                  .HasForeignKey(e => e.EventId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Agent记忆
        modelBuilder.Entity<AgentMemoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AgentId, e.StockCode, e.CreatedAt });
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.CreatedAt);
        });

        // Prompt模板
        modelBuilder.Entity<PromptTemplateEntity>(entity =>
        {
            entity.HasKey(e => new { e.Name, e.Version });
            entity.HasIndex(e => e.Category);
        });

        // 龙虎榜
        modelBuilder.Entity<DragonTigerEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Code, e.Date }).IsUnique();
            entity.HasIndex(e => e.Date);
        });

        // 每日市场快照
        modelBuilder.Entity<DailyMarketSnapshotEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Code, e.Date }).IsUnique();
            entity.HasIndex(e => e.Date);
        });

        // 龙虎榜席位明细（先删后插控制重复，不设唯一键；按席位/日期/代码查询）
        modelBuilder.Entity<DragonTigerSeatEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Code, e.Date });
            entity.HasIndex(e => e.SeatName);
            entity.HasIndex(e => e.Date);
        });
    }
}
