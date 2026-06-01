using AIStock.Core.Enums;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Intelligence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStock.Tests;

/// <summary>
/// 传播链分析单元测试（EF InMemory，脱离真实 MySQL/LLM/采集）。
///
/// 注意：InMemory 是 LINQ-to-Objects，与 Pomelo+MySQL 的 SQL 翻译行为不完全一致：
///   - 实现里 `e.Content!.Contains(keyword)` 在 InMemory 下遇 Content=null 会抛 NRE，
///     真实 MySQL 翻译成 LIKE 不会。因此所有测试数据的 Content 均赋非 null 值。
///   - GroupBy(CreatedAt.Hour) 在 InMemory 下是内存分组，能验证业务逻辑，但不代表 MySQL 的 HOUR() 行为。
/// </summary>
public class SpreadAnalyzerServiceTests
{
    private static AIStockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AIStockDbContext>()
            .UseInMemoryDatabase($"spread-{Guid.NewGuid()}")
            .Options);

    private static SpreadAnalyzerService NewService(AIStockDbContext db) =>
        new(db, NullLogger<SpreadAnalyzerService>.Instance);

    private static EventRecordEntity Event(string title, string source, DateTime eventTime,
        string eventType = "news", int? importance = null, string? content = null) =>
        new()
        {
            EventType = eventType,
            Title = title,
            Content = content ?? title, // 避免 InMemory 对 null Content 的 NRE
            Source = source,
            EventTime = eventTime,
            Importance = importance
        };

    [Fact]
    public async Task AnalyzeSpread_EmptyDb_ReturnsNotFound()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var result = await svc.AnalyzeSpreadAsync("光伏");

        Assert.Equal("未找到相关事件", result.Conclusion);
        Assert.Empty(result.SpreadPath);
        Assert.Null(result.FirstSource);
    }

    [Fact]
    public async Task AnalyzeSpread_MultiSource_IdentifiesFirstSourceByEventTime()
    {
        using var db = NewDb();
        var baseT = new DateTime(2026, 5, 30, 9, 0, 0, DateTimeKind.Utc);
        // 故意乱序插入：最早的 EventTime（雪球）放在中间，验证按 EventTime 而非插入顺序定首发源
        db.EventRecord.AddRange(
            Event("某公司光伏新产能落地", "财联社", baseT.AddHours(2)),
            Event("某公司光伏项目传闻", "雪球", baseT),                 // 最早
            Event("某公司光伏获机构调研", "东方财富", baseT.AddHours(1)));
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var result = await svc.AnalyzeSpreadAsync("光伏");

        Assert.Equal("雪球", result.FirstSource);
        Assert.Equal(baseT, result.FirstTime);
        // 传播路径按 EventTime 升序
        Assert.Equal(3, result.SpreadPath.Count);
        Assert.Equal(new[] { "雪球", "东方财富", "财联社" },
            result.SpreadPath.Select(n => n.Source).ToArray());
    }

    [Fact]
    public async Task AnalyzeSpread_KeywordFiltersUnrelatedEvents()
    {
        using var db = NewDb();
        var t = new DateTime(2026, 5, 30, 9, 0, 0, DateTimeKind.Utc);
        db.EventRecord.AddRange(
            Event("光伏龙头扩产", "财联社", t),
            Event("白酒提价公告", "新浪财经", t.AddHours(1))); // 不含关键词，应被过滤
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var result = await svc.AnalyzeSpreadAsync("光伏");

        Assert.Single(result.SpreadPath);
        Assert.Equal("财联社", result.SpreadPath[0].Source);
    }

    [Fact]
    public async Task AnalyzeSpread_Influence_PolicyHigherThanNews()
    {
        using var db = NewDb();
        var t = new DateTime(2026, 5, 30, 9, 0, 0, DateTimeKind.Utc);
        // policy +20、news 基础 50；Importance 叠加
        db.EventRecord.Add(Event("光伏政策利好", "发改委", t, eventType: "policy", importance: 10));
        db.EventRecord.Add(Event("光伏普通新闻", "某站", t.AddHours(1), eventType: "news"));
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var result = await svc.AnalyzeSpreadAsync("光伏");

        var policy = result.SpreadPath.Single(n => n.Source == "发改委");
        var news = result.SpreadPath.Single(n => n.Source == "某站");
        Assert.True(policy.Influence > news.Influence);
        Assert.Equal(80, policy.Influence);  // 50 + 20(policy) + 10(importance)
        Assert.Equal(50, news.Influence);     // 50 基础
    }

    [Fact]
    public async Task AnalyzeSpread_Conclusion_ContainsFirstSourceAndHeat()
    {
        using var db = NewDb();
        var t = DateTime.Now.AddMinutes(-30); // 近 24h 内，保证 CurrentHeat 有效
        db.EventRecord.Add(Event("光伏大利好", "财联社", t));
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var result = await svc.AnalyzeSpreadAsync("光伏");

        Assert.Contains("首发源：财联社", result.Conclusion);
        Assert.Contains("当前热度", result.Conclusion);
    }

    [Fact]
    public async Task CalculateHeatSlope_LessThanTwoBuckets_ReturnsZero()
    {
        using var db = NewDb();
        db.EventRecord.Add(Event("光伏", "财联社", DateTime.Now.AddMinutes(-10)));
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var slope = await svc.CalculateHeatSlopeAsync("光伏");

        // 单条事件只有一个小时桶，无法回归 → 0
        Assert.Equal(0, slope);
    }
}
