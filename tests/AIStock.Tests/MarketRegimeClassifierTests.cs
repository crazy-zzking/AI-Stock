using AIStock.Core.Models;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>市场状态分类器测试：4 态判定与推荐策略。</summary>
public class MarketRegimeClassifierTests
{
    [Fact]
    public void RiskOff_OnIndexCrash()
    {
        var r = MarketRegimeClassifier.Classify(
            avgIndexChange: -1.5m, indicesAboveMa20: 0, indexCount: 6,
            advanceRatio: 0.25m, limitUpCount: 5, limitDownCount: 40, totalStocks: 5000);
        Assert.Equal(RegimeKind.RiskOff, r.Kind);
        Assert.Equal("lowdip", r.RecommendedStrategy);
    }

    [Fact]
    public void TrendUp_OnBroadStrength()
    {
        var r = MarketRegimeClassifier.Classify(
            avgIndexChange: 1.2m, indicesAboveMa20: 5, indexCount: 6,
            advanceRatio: 0.68m, limitUpCount: 25, limitDownCount: 2, totalStocks: 5000);
        Assert.Equal(RegimeKind.TrendUp, r.Kind);
        Assert.Equal("trend", r.RecommendedStrategy);
    }

    [Fact]
    public void ThemeMarket_OnLimitUpTideWithFlatIndex()
    {
        // 指数平、广度一般，但涨停潮（60/5000 = 1.2%）→ 题材主导
        var r = MarketRegimeClassifier.Classify(
            avgIndexChange: 0.2m, indicesAboveMa20: 3, indexCount: 6,
            advanceRatio: 0.5m, limitUpCount: 60, limitDownCount: 5, totalStocks: 5000);
        Assert.Equal(RegimeKind.ThemeMarket, r.Kind);
        Assert.Equal("theme", r.RecommendedStrategy);
    }

    [Fact]
    public void Range_OnQuietMarket()
    {
        var r = MarketRegimeClassifier.Classify(
            avgIndexChange: 0.1m, indicesAboveMa20: 3, indexCount: 6,
            advanceRatio: 0.5m, limitUpCount: 10, limitDownCount: 5, totalStocks: 5000);
        Assert.Equal(RegimeKind.Range, r.Kind);
        Assert.Equal("lowdip", r.RecommendedStrategy);
    }

    [Fact]
    public void TrendUp_TakesPrecedenceOverTheme()
    {
        // 既普涨强势又涨停多 → 优先趋势上涨
        var r = MarketRegimeClassifier.Classify(
            avgIndexChange: 1.0m, indicesAboveMa20: 6, indexCount: 6,
            advanceRatio: 0.7m, limitUpCount: 80, limitDownCount: 1, totalStocks: 5000);
        Assert.Equal(RegimeKind.TrendUp, r.Kind);
    }
}
