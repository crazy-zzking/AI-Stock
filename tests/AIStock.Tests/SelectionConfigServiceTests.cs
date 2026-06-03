using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Selection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStock.Tests;

/// <summary>
/// 选股配置中心测试（EF InMemory）：版本保存 / 激活切换 / 生效回退 / 删除保护。
/// </summary>
public class SelectionConfigServiceTests
{
    private static AIStockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AIStockDbContext>()
            .UseInMemoryDatabase($"sel-cfg-{Guid.NewGuid()}")
            .Options);

    private static SelectionConfigService NewSvc(AIStockDbContext db) =>
        new(db, NullLogger<SelectionConfigService>.Instance);

    [Fact]
    public async Task GetActiveCriteria_NoConfig_ReturnsCodeDefault()
    {
        using var db = NewDb();
        var criteria = await NewSvc(db).GetActiveCriteriaAsync();

        // 与代码默认一致
        Assert.Equal(new SelectionCriteria().MaxRsi, criteria.MaxRsi);
        Assert.Equal(new SelectionCriteria().TopN, criteria.TopN);
    }

    [Fact]
    public async Task Save_WithActivate_ThenGetActive_RoundTrips()
    {
        using var db = NewDb();
        var svc = NewSvc(db);

        var criteria = new SelectionCriteria { MaxRsi = 65m, TopN = 8 };
        criteria.Weights.Capital = 0.30m;

        await svc.SaveAsync(SelectionConfigService.DefaultName, "v1.0", criteria, "调高资金权重", activate: true);

        var active = await svc.GetActiveCriteriaAsync();
        Assert.Equal(65m, active.MaxRsi);
        Assert.Equal(8, active.TopN);
        Assert.Equal(0.30m, active.Weights.Capital);
    }

    [Fact]
    public async Task Activate_NewVersion_DeactivatesOld()
    {
        using var db = NewDb();
        var svc = NewSvc(db);

        await svc.SaveAsync(SelectionConfigService.DefaultName, "v1.0",
            new SelectionCriteria { MaxRsi = 60m }, null, activate: true);
        var v2 = await svc.SaveAsync(SelectionConfigService.DefaultName, "v1.1",
            new SelectionCriteria { MaxRsi = 75m }, null, activate: true);

        // 激活 v1.1 后，生效配置为 v1.1，且仅一条 IsActive
        var active = await svc.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal("v1.1", active!.Version);

        var all = await svc.ListAsync();
        Assert.Single(all, c => c.IsActive);
        Assert.Equal(v2.Id, all.Single(c => c.IsActive).Id);
    }

    [Fact]
    public async Task Delete_ActiveVersion_IsRejected()
    {
        using var db = NewDb();
        var svc = NewSvc(db);

        var saved = await svc.SaveAsync(SelectionConfigService.DefaultName, "v1.0",
            new SelectionCriteria(), null, activate: true);

        var deleted = await svc.DeleteAsync(saved.Id);
        Assert.False(deleted); // 生效中不可删

        var still = await svc.GetByIdAsync(saved.Id);
        Assert.NotNull(still);
    }

    [Fact]
    public async Task Save_SameNameVersion_OverwritesJson()
    {
        using var db = NewDb();
        var svc = NewSvc(db);

        await svc.SaveAsync(SelectionConfigService.DefaultName, "v1.0",
            new SelectionCriteria { MaxRsi = 60m }, null, activate: true);
        await svc.SaveAsync(SelectionConfigService.DefaultName, "v1.0",
            new SelectionCriteria { MaxRsi = 72m }, "修订", activate: true);

        var all = await svc.ListAsync();
        Assert.Single(all); // 同 name+version 覆盖而非新增
        var active = await svc.GetActiveCriteriaAsync();
        Assert.Equal(72m, active.MaxRsi);
    }
}
