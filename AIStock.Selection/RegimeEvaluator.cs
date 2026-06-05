using AIStock.Core.Models;

namespace AIStock.Selection;

/// <summary>
/// 大盘环境评估（纯函数）— 实盘选股与历史回放共用同一口径，保证 Level/Kind 判定一致。
/// 综合指数(均涨跌+站上20线) + 全市场广度 + 涨停跌停。指数缺失时仅用广度（更保守，达不到强/弱）。
/// </summary>
public static class RegimeEvaluator
{
    /// <summary>参与判断的主要指数（名称 + 东财 secid，secid 同时作为 kline_data 中指数行的 code）。</summary>
    public static readonly (string Name, string Secid)[] MarketIndices =
    {
        ("上证", "1.000001"),
        ("深成", "0.399001"),
        ("创业", "0.399006"),
        ("沪深300", "1.000300"),
        ("科创50", "1.000688"),
        ("北证50", "0.899050"),
    };

    /// <summary>由升序收盘价序列构造指数行情（当日涨跌幅 + 是否站上20日线）。不足2根返回 null。</summary>
    public static IndexQuote? QuoteFromCloses(string name, IReadOnlyList<decimal> closesAsc)
    {
        if (closesAsc.Count < 2) return null;
        var last = closesAsc[^1];
        var prev = closesAsc[^2];
        var ma20 = closesAsc.Count >= 20 ? closesAsc.TakeLast(20).Average() : closesAsc.Average();
        var close20Ago = closesAsc.Count >= 21 ? closesAsc[^21] : closesAsc[0];
        return new IndexQuote
        {
            Name = name,
            ChangePercent = prev > 0 ? Math.Round((last - prev) / prev * 100m, 2) : 0,
            AboveMa20 = last >= ma20,
            Rise20d = close20Ago > 0 ? Math.Round((last - close20Ago) / close20Ago * 100m, 2) : 0,
        };
    }

    /// <summary>综合评估 Level（强/中/弱）+ Kind（4态）+ 推荐策略 + 状态标签。</summary>
    public static (MarketRegimeLevel Level, RegimeKind Kind, string RecommendedStrategy, string KindLabel) Evaluate(
        IReadOnlyList<IndexQuote> indices, decimal advanceRatio, int limitUpCount, int limitDownCount, int totalStocks)
    {
        var score = 0;
        decimal avgChange = 0;
        var aboveCount = 0;
        if (indices.Count > 0)
        {
            avgChange = indices.Average(i => i.ChangePercent);
            if (avgChange > 0.5m) score++;
            else if (avgChange < -0.5m) score--;

            aboveCount = indices.Count(i => i.AboveMa20);
            var half = indices.Count / 2.0;
            if (aboveCount > half) score++;
            else if (aboveCount < half) score--;
        }
        if (advanceRatio > 0.55m) score++;
        else if (advanceRatio is > 0m and < 0.4m) score--;

        var level = score >= 2 ? MarketRegimeLevel.Strong
            : score <= -2 ? MarketRegimeLevel.Weak
            : MarketRegimeLevel.Neutral;

        var cls = MarketRegimeClassifier.Classify(
            avgChange, aboveCount, indices.Count, advanceRatio, limitUpCount, limitDownCount, totalStocks);
        return (level, cls.Kind, cls.RecommendedStrategy, cls.Label);
    }
}
