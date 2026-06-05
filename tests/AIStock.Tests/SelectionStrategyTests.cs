using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Narration;
using AIStock.Selection.Strategies;

namespace AIStock.Tests;

/// <summary>
/// 多策略口径测试（纯计算）：验证 Trend 容忍高 RSI、要求站上均线；Theme 要求命中热门题材；
/// 与 LowDip 形成可区分的行为差异。
/// </summary>
public class SelectionStrategyTests
{
    private static DailyMarketSnapshotEntity Snap(
        string code, decimal rsi = 60m, decimal mainNet = 30_000_000m, decimal rise20d = 15m,
        decimal close = 11m, decimal ma5 = 10.8m, decimal ma10 = 10.4m, decimal ma20 = 10m,
        bool limitUp = false, bool macdGolden = true) =>
        new()
        {
            Code = code, Name = code, Date = new DateTime(2026, 5, 12),
            Close = close, Ma5 = ma5, Ma10 = ma10, Ma20 = ma20,
            MainNetInflow = mainNet, Rise20d = rise20d, Rsi = rsi,
            MacdGoldenCross = macdGolden, MacdDif = 1.1m, MacdDea = -0.03m, IsLimitUp = limitUp,
        };

    private static ActivityScreener.ActivityHit Hit(DailyMarketSnapshotEntity s, decimal score = 55m)
        => new(s, score, new List<string>());

    private static readonly Dictionary<string, DragonTigerEntity> NoDragon = new();
    private static readonly Dictionary<string, SequenceFeatures> NoSeq = new();

    private static LowDipStrategy LowDip() => new(new StockSelectionEngine(new RuleLogicNarrator()));

    [Fact]
    public void Trend_AllowsHighRsi_WhereLowDipExcludes()
    {
        // RSI 80 强势股：LowDip 超买淘汰，Trend 容忍并选中
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", rsi: 80m)) };

        var lowdip = LowDip().Select(pool, NoDragon, NoSeq, new SelectionCriteria());
        var trend = new TrendStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria());

        Assert.Empty(lowdip);                 // 超买被 LowDip 淘汰
        var r = Assert.Single(trend);         // 趋势策略选中
        Assert.Contains("强势RSI", r.Tags);
    }

    [Fact]
    public void Trend_BelowMa20_IsExcluded()
    {
        // 跌破 20 日线：趋势策略要求站上中期均线
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", rsi: 65m, close: 9m, ma20: 10m)) };

        var trend = new TrendStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria());

        Assert.Empty(trend);
    }

    [Fact]
    public void Theme_RequiresHotConcept()
    {
        // A 命中热门题材，B 不命中 → 题材策略只选 A
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")), Hit(Snap("B")) };
        var ctx = new SelectionContext
        {
            ConceptsByCode = new Dictionary<string, List<string>> { ["A"] = new() { "人工智能" }, ["B"] = new() { "冷门概念" } },
            HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
        };

        var theme = new ThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), ctx);

        var r = Assert.Single(theme);
        Assert.Equal("A", r.Code);
        Assert.Contains(r.Tags, t => t.Contains("风口"));
    }

    [Fact]
    public void Theme_NoContext_SelectsNothing()
    {
        // 无题材上下文 → 题材策略无标的（硬门槛是命中热门题材）
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")) };

        var theme = new ThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria());

        Assert.Empty(theme);
    }

    [Fact]
    public void Strategies_HaveDistinctKeys()
    {
        var keys = new[] { LowDip().Key, new TrendStrategy().Key, new ThemeStrategy().Key };
        Assert.Equal(new[] { "lowdip", "trend", "theme" }, keys);
        Assert.Equal(3, keys.Distinct().Count());
    }

    // —— 可配置策略框架等价性：用 ConfigurableSelectionStrategy + 内置定义 复刻内置策略，结果须逐项一致 ——

    private static void AssertSameSelection(
        IReadOnlyList<StockSelectionResult> expected, IReadOnlyList<StockSelectionResult> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Code, actual[i].Code);
            Assert.Equal(expected[i].TotalScore, actual[i].TotalScore);
            Assert.Equal(expected[i].RatingStars, actual[i].RatingStars);
            Assert.Equal(expected[i].Factors.Technical, actual[i].Factors.Technical);
            Assert.Equal(expected[i].Factors.Position, actual[i].Factors.Position);
            Assert.Equal(expected[i].Factors.Capital, actual[i].Factors.Capital);
        }
    }

    private static List<ActivityScreener.ActivityHit> VariedPool() => new()
    {
        Hit(Snap("A", rsi: 75m, rise20d: 30m, close: 12m, ma20: 10m)),
        Hit(Snap("B", rsi: 60m, rise20d: 10m)),
        Hit(Snap("C", rsi: 50m, rise20d: 45m, close: 13m, mainNet: 80_000_000m)),
        Hit(Snap("D", rsi: 65m, close: 9m, ma20: 10m)),   // 跌破MA20：趋势硬过滤剔除
        Hit(Snap("E", rsi: 30m, rise20d: 5m)),
    };

    [Fact]
    public void Configurable_ReplicatesBuiltinTrend()
    {
        var pool = VariedPool();
        var crit = new SelectionCriteria { TopN = 10 };

        var builtin = new TrendStrategy().Select(pool, NoDragon, NoSeq, crit);
        var configurable = new ConfigurableSelectionStrategy(BuiltinStrategyDefinitions.Trend())
            .Select(pool, NoDragon, NoSeq, crit);

        Assert.NotEmpty(builtin);
        AssertSameSelection(builtin, configurable);
    }

    [Fact]
    public void Configurable_ReplicatesBuiltinLowDip()
    {
        var pool = VariedPool();
        var crit = new SelectionCriteria { TopN = 10 };

        var builtin = LowDip().Select(pool, NoDragon, NoSeq, crit);
        var configurable = new ConfigurableSelectionStrategy(BuiltinStrategyDefinitions.LowDip())
            .Select(pool, NoDragon, NoSeq, crit);

        Assert.NotEmpty(builtin);
        AssertSameSelection(builtin, configurable);
    }

    [Fact]
    public void Configurable_ReplicatesBuiltinTheme()
    {
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")), Hit(Snap("B")) };
        var ctx = new SelectionContext
        {
            ConceptsByCode = new Dictionary<string, List<string>> { ["A"] = new() { "人工智能" }, ["B"] = new() { "冷门概念" } },
            HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
        };
        var crit = new SelectionCriteria { TopN = 10 };

        var builtin = new ThemeStrategy().Select(pool, NoDragon, NoSeq, crit, ctx);
        var configurable = new ConfigurableSelectionStrategy(BuiltinStrategyDefinitions.Theme())
            .Select(pool, NoDragon, NoSeq, crit, ctx);

        Assert.NotEmpty(builtin);
        AssertSameSelection(builtin, configurable);
    }
}
