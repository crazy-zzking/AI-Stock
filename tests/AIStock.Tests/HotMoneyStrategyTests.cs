using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Strategies;

namespace AIStock.Tests;

/// <summary>
/// 题材动量策略测试（纯计算）：验证硬过滤=命中热门题材 + 量比≥阈值 + 站上BBI + RSI未超买 + 主力净流入。
/// </summary>
public class HotMoneyStrategyTests
{
    private static DailyMarketSnapshotEntity Snap(
        string code, decimal volumeRatio, decimal close = 11m, decimal bbi = 0m,
        decimal rsi = 60m, decimal mainNet = 30_000_000m,
        decimal ma5 = 10.8m, decimal ma10 = 10.4m, decimal ma20 = 10m) =>
        new()
        {
            Code = code, Name = code, Date = new DateTime(2026, 5, 12),
            Close = close, Ma5 = ma5, Ma10 = ma10, Ma20 = ma20, Bbi = bbi,
            MainNetInflow = mainNet, Rsi = rsi, VolumeRatio = volumeRatio,
            MacdGoldenCross = true, MacdDif = 1.1m, MacdDea = -0.03m,
        };

    private static ActivityScreener.ActivityHit Hit(DailyMarketSnapshotEntity s) => new(s, 55m, new List<string>());
    private static readonly Dictionary<string, DragonTigerEntity> NoDragon = new();
    private static readonly Dictionary<string, SequenceFeatures> NoSeq = new();

    private static SelectionContext HotCtx(params string[] codes)
        => new()
        {
            ConceptsByCode = codes.ToDictionary(c => c, _ => new List<string> { "人工智能" }),
            HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
        };

    [Fact]
    public void RequiresHotConcept()
    {
        // A 命中热门题材，B 不命中 → 只选 A
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", 2.0m)), Hit(Snap("B", 2.0m)) };
        var ctx = new SelectionContext
        {
            ConceptsByCode = new Dictionary<string, List<string>> { ["A"] = new() { "人工智能" }, ["B"] = new() { "冷门概念" } },
            HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
        };

        var r = Assert.Single(new HotMoneyStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), ctx));
        Assert.Equal("A", r.Code);
    }

    [Fact]
    public void LowVolumeRatio_IsExcluded()
    {
        // 量比 1.0 < 默认门槛 1.5 → 剔除（即便命中题材）
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", 1.0m)) };
        Assert.Empty(new HotMoneyStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), HotCtx("A")));
    }

    [Fact]
    public void BelowBbi_IsExcluded()
    {
        // 收盘 10 跌破 BBI 11 → 剔除；量比/题材均达标
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", 2.0m, close: 10m, bbi: 11m)) };
        Assert.Empty(new HotMoneyStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), HotCtx("A")));
    }

    [Fact]
    public void AboveBbi_WithAllConditions_IsSelected()
    {
        // 收盘 11 站上 BBI 10 + 量比达标 + 命中题材 → 选中，带"站上BBI"标签
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", 2.0m, close: 11m, bbi: 10m)) };
        var r = Assert.Single(new HotMoneyStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), HotCtx("A")));
        Assert.Equal("A", r.Code);
        Assert.Contains("站上BBI", r.Tags);
    }

    [Fact]
    public void Overbought_IsExcluded()
    {
        // RSI 80 > 默认 MaxRsi 70 → 剔除（未超买是硬门槛）
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", 2.0m, rsi: 80m)) };
        Assert.Empty(new HotMoneyStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), HotCtx("A")));
    }

    [Fact]
    public void Key_IsHotMoney() => Assert.Equal("hotmoney", new HotMoneyStrategy().Key);
}
