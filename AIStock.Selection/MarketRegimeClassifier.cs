using AIStock.Core.Models;
using AIStock.Selection.Strategies;

namespace AIStock.Selection;

/// <summary>
/// 市场状态分类器（纯函数）。综合指数涨跌、站上 20 线比例、全市场涨跌广度、涨停/跌停家数，
/// 判定 4 种市场状态并给出推荐策略。阈值为经验值，可后续按回测校准。
/// </summary>
public static class MarketRegimeClassifier
{
    public sealed record Result(RegimeKind Kind, string RecommendedStrategy, string Label);

    public static Result Classify(
        decimal avgIndexChange, int indicesAboveMa20, int indexCount,
        decimal advanceRatio, int limitUpCount, int limitDownCount, int totalStocks)
    {
        var limitUpRatio = totalStocks > 0 ? (decimal)limitUpCount / totalStocks : 0m;
        var aboveMa20Majority = indexCount > 0 && indicesAboveMa20 * 2 >= indexCount;

        // 1) 风险释放：指数大跌 / 涨家极少 / 跌停明显多于涨停
        if (avgIndexChange <= -1.0m || advanceRatio is > 0m and < 0.3m
            || (limitDownCount >= 20 && limitDownCount > limitUpCount))
            return new Result(RegimeKind.RiskOff, StrategyKeys.LowDip, "风险释放（防守/低吸）");

        // 2) 趋势上涨：指数走强 + 多数站上 20 线 + 普涨扩散
        if (avgIndexChange >= 0.8m && advanceRatio >= 0.6m && aboveMa20Majority)
            return new Result(RegimeKind.TrendUp, StrategyKeys.Trend, "趋势上涨（趋势跟随）");

        // 3) 题材主导：涨停潮（结构性活跃），指数未必强
        if (limitUpRatio >= 0.012m && limitUpCount >= 30)
            return new Result(RegimeKind.ThemeMarket, StrategyKeys.Theme, "题材主导（题材龙头）");

        // 4) 其余：震荡市
        return new Result(RegimeKind.Range, StrategyKeys.LowDip, "震荡市（低吸埋伏）");
    }
}
