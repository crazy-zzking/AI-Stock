using AIStock.Core.Models;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>大盘环境评估器测试：指数+广度综合判定 Level；无指数时更保守。</summary>
public class RegimeEvaluatorTests
{
    private static List<IndexQuote> Indices(decimal change, bool above, int n = 6)
        => Enumerable.Range(0, n).Select(_ => new IndexQuote { ChangePercent = change, AboveMa20 = above }).ToList();

    [Fact]
    public void Evaluate_BroadStrength_IsStrong()
    {
        var (level, _, _, _) = RegimeEvaluator.Evaluate(Indices(1.0m, true), 0.6m, 30, 2, 5000);
        Assert.Equal(MarketRegimeLevel.Strong, level);
    }

    [Fact]
    public void Evaluate_BroadWeakness_IsWeak()
    {
        var (level, _, _, _) = RegimeEvaluator.Evaluate(Indices(-1.0m, false), 0.3m, 3, 40, 5000);
        Assert.Equal(MarketRegimeLevel.Weak, level);
    }

    [Fact]
    public void Evaluate_NoIndex_StaysNeutral_EvenWithGoodBreadth()
    {
        // 无指数时仅广度最多 ±1，达不到 ±2 → 保守 Neutral
        var (level, _, _, _) = RegimeEvaluator.Evaluate(new List<IndexQuote>(), 0.7m, 20, 2, 5000);
        Assert.Equal(MarketRegimeLevel.Neutral, level);
    }

    [Fact]
    public void QuoteFromCloses_ComputesChangeAndMa()
    {
        var q = RegimeEvaluator.QuoteFromCloses("测试", new List<decimal> { 10m, 11m });
        Assert.NotNull(q);
        Assert.Equal(10m, q!.ChangePercent);   // (11-10)/10
        Assert.True(q.AboveMa20);              // 11 >= 均值10.5
    }

    [Fact]
    public void QuoteFromCloses_TooFewBars_ReturnsNull()
        => Assert.Null(RegimeEvaluator.QuoteFromCloses("x", new List<decimal> { 10m }));
}
