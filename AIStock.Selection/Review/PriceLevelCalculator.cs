using AIStock.Core.Models;

namespace AIStock.Selection.Review;

/// <summary>
/// 买入计划价位计算器 —— 纯规则、确定性、可复现（不调 LLM）。
/// 思路：以选股日收盘为基准，结合 MA5/近5日低点（支撑）与 ATR（波动），
/// 给出"回踩不追高"的买入区间、跌破支撑的止损、盈亏比≈2:1 的止盈。
/// </summary>
public static class PriceLevelCalculator
{
    /// <summary>一根日 K（计算所需字段）</summary>
    public readonly record struct PriceBar(decimal High, decimal Low, decimal Close);

    /// <summary>
    /// 计算买入计划。bars 需按时间升序；baseClose 为选股日收盘价（基准）。
    /// 数据不足时退化为百分比法。返回的 Basis 仅含规则依据，LLM 解释由上层追加。
    /// </summary>
    public static TradePlan Compute(IReadOnlyList<PriceBar> bars, decimal baseClose)
    {
        if (baseClose <= 0)
            return new TradePlan { Basis = "无有效基准价，跳过价位计算" };

        decimal Round(decimal v) => Math.Round(v, 2);

        // 数据不足：百分比兜底
        if (bars == null || bars.Count < 5)
        {
            var bl = Round(baseClose * 0.97m);
            var bh = Round(baseClose * 1.02m);
            var sl = Round(baseClose * 0.93m);
            var tp = Round(baseClose * 1.14m);
            return new TradePlan
            {
                BuyLow = bl, BuyHigh = bh, StopLoss = sl, TakeProfit = tp,
                Basis = $"K线不足，按基准{baseClose:F2}百分比估算（区间-3%~+2%，止损-7%，止盈+14%，盈亏比≈2:1）",
            };
        }

        var last5 = bars.Skip(Math.Max(0, bars.Count - 5)).ToList();
        var ma5 = last5.Average(b => b.Close);
        var recent5Low = last5.Min(b => b.Low);

        // 支撑：MA5 与近5日低点取高者；若高于基准则回落到 -3%
        var support = Math.Max(ma5, recent5Low);
        if (support >= baseClose) support = baseClose * 0.97m;

        // ATR(最多14根)：真实波幅均值
        var atrWindow = bars.Skip(Math.Max(1, bars.Count - 14)).ToList();
        decimal atrSum = 0; int atrN = 0;
        for (int i = Math.Max(1, bars.Count - 14); i < bars.Count; i++)
        {
            var cur = bars[i]; var prevClose = bars[i - 1].Close;
            var tr = Math.Max(cur.High - cur.Low,
                     Math.Max(Math.Abs(cur.High - prevClose), Math.Abs(cur.Low - prevClose)));
            atrSum += tr; atrN++;
        }
        var atr = atrN > 0 ? atrSum / atrN : baseClose * 0.03m;

        // 买入区间：下沿=支撑与-4%取高者（回踩买）；上沿=基准+2%（不追高）
        var buyLow = Round(Math.Max(support, baseClose * 0.96m));
        var buyHigh = Round(baseClose * 1.02m);
        if (buyLow > buyHigh) buyLow = Round(baseClose * 0.98m);

        // 止损：跌破支撑半个 ATR；最大亏损限制在基准 -9% 以内
        var rawStop = Math.Min(support, buyLow) - atr * 0.5m;
        var stopFloor = baseClose * 0.91m;
        var stop = Round(Math.Max(rawStop, stopFloor));
        if (stop >= buyLow) stop = Round(buyLow * 0.97m);

        // 止盈：以基准为入场，盈亏比≈2:1
        var risk = baseClose - stop;
        if (risk <= 0) risk = baseClose * 0.07m;
        var target = Round(baseClose + 2m * risk);

        return new TradePlan
        {
            BuyLow = buyLow, BuyHigh = buyHigh, StopLoss = stop, TakeProfit = target,
            Basis = $"基准{baseClose:F2}，支撑{support:F2}(MA5/5日低)，ATR{atr:F2}；" +
                    $"区间{buyLow:F2}~{buyHigh:F2}(回踩不追高)，止损{stop:F2}，止盈{target:F2}(盈亏比≈2:1)",
        };
    }
}
