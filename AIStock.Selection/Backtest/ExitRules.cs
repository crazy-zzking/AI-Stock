using AIStock.Core.Models;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 出场规则构造器（纯函数）。每条规则返回一个带 Evaluator 的 ExitRule 实例。
/// 规则按 Priority 升序执行——止损类通常 Priority 更低（更早触发）。
/// </summary>
public static class ExitRules
{
    /// <summary>
    /// 固定止损：持仓期间当日最低价 ≤ 入场价 × (1 - pct%) 即触发。
    /// 跳空低开按开盘价成交（比止损价更差）。
    /// </summary>
    public static ExitRule FixedStopLoss(decimal pct, int priority = 1) => new()
    {
        Type = ExitRuleType.FixedStopLoss,
        Param1 = pct,
        Priority = priority,
        Evaluator = ctx =>
        {
            var stopPrice = ctx.EntryPrice * (1 - pct / 100m);
            if (ctx.Today.Low <= stopPrice)
                return new ExitResult
                {
                    ExitPrice = Math.Min(ctx.Today.Open, stopPrice),
                    Reason = "stoploss"
                };
            return null;
        }
    };

    /// <summary>
    /// 固定止盈：持仓期间当日最高价 ≥ 入场价 × (1 + pct%) 即触发。
    /// 跳空高开按开盘价成交（比止盈价更优）。
    /// </summary>
    public static ExitRule FixedTakeProfit(decimal pct, int priority = 2) => new()
    {
        Type = ExitRuleType.FixedTakeProfit,
        Param1 = pct,
        Priority = priority,
        Evaluator = ctx =>
        {
            var tpPrice = ctx.EntryPrice * (1 + pct / 100m);
            if (ctx.Today.High >= tpPrice)
                return new ExitResult
                {
                    ExitPrice = Math.Max(ctx.Today.Open, tpPrice),
                    Reason = "takeprofit"
                };
            return null;
        }
    };

    /// <summary>
    /// 移动止损：从持仓期间最高价回落 pct% 即触发。
    /// 最高价逐日更新（在止损检查之前已更新，不会漏判）。
    /// </summary>
    public static ExitRule TrailingStop(decimal drawdownPct, int priority = 3) => new()
    {
        Type = ExitRuleType.TrailingStop,
        Param1 = drawdownPct,
        Priority = priority,
        Evaluator = ctx =>
        {
            var stopPrice = ctx.HighestHigh * (1 - drawdownPct / 100m);
            if (ctx.Today.Low <= stopPrice)
                return new ExitResult
                {
                    ExitPrice = Math.Min(ctx.Today.Open, stopPrice),
                    Reason = "trailing_stop"
                };
            return null;
        }
    };

    /// <summary>
    /// 移动止盈：浮盈曾达到 activatePct% 后激活（HighestHigh 单调不减，激活后恒激活），
    /// 自持仓期间最高价回落 drawdownPct% 即触发，锁定利润。
    /// 跳空低开按开盘价成交（比触发价更差）。
    /// </summary>
    public static ExitRule TrailingTakeProfit(decimal activatePct, decimal drawdownPct, int priority = 2) => new()
    {
        Type = ExitRuleType.TrailingTakeProfit,
        Param1 = activatePct,
        Param2 = drawdownPct,
        Priority = priority,
        Evaluator = ctx =>
        {
            // 浮盈未曾达到激活线 → 不止盈
            if (ctx.HighestHigh < ctx.EntryPrice * (1 + activatePct / 100m)) return null;
            var triggerPrice = ctx.HighestHigh * (1 - drawdownPct / 100m);
            if (ctx.Today.Low <= triggerPrice)
                return new ExitResult
                {
                    ExitPrice = Math.Min(ctx.Today.Open, triggerPrice),
                    Reason = "trailing_take_profit"
                };
            return null;
        }
    };

    /// <summary>
    /// ATR 动态止损：入场价 − multiplier × ATR(14) 即触发。
    /// ATR(14) 在入场时计算（基于入场前 14 根日 K）。
    /// </summary>
    public static ExitRule AtrStop(decimal multiplier, int priority = 4) => new()
    {
        Type = ExitRuleType.AtrStop,
        Param1 = multiplier,
        Priority = priority,
        Evaluator = ctx =>
        {
            if (ctx.Atr14 <= 0) return null; // ATR 不可用则跳过
            var stopPrice = ctx.EntryPrice - ctx.Atr14 * multiplier;
            if (ctx.Today.Low <= stopPrice)
                return new ExitResult
                {
                    ExitPrice = Math.Min(ctx.Today.Open, stopPrice),
                    Reason = "atr_stop"
                };
            return null;
        }
    };

    /// <summary>
    /// MA 死叉出场：当日收盘价 < MA(period) 即触发。
    /// period 通过 Param1 指定（如 20 → MA20）。
    /// </summary>
    public static ExitRule MaExit(int period, int priority = 5) => new()
    {
        Type = ExitRuleType.MaCrossDown,
        Param1 = period,
        Priority = priority,
        Evaluator = ctx =>
        {
            if (ctx.MaValue <= 0) return null;
            if (ctx.Today.Close < ctx.MaValue)
                return new ExitResult
                {
                    ExitPrice = ctx.Today.Close,
                    Reason = $"ma{period}_exit"
                };
            return null;
        }
    };

    /// <summary>
    /// 时间止盈：持有 ≥ maxDays 个交易日则到期卖出（替代原 HoldDays）。
    /// 通常放在最后（Priority=99），作为兜底出场。
    /// </summary>
    public static ExitRule TimeExit(int maxDays, int priority = 99) => new()
    {
        Type = ExitRuleType.TimeExit,
        Param1 = maxDays,
        Priority = priority,
        Evaluator = ctx =>
        {
            if (ctx.DaysHeld >= maxDays)
                return new ExitResult
                {
                    ExitPrice = ctx.Today.Close,
                    Reason = "time"
                };
            return null;
        }
    };

    /// <summary>
    /// 放量出场：当日成交量 > 入场前N日均量 × multiplier 倍。
    /// Param1 = 均量天数 (如20)，Param2 = 倍数 (如2.0)。
    /// </summary>
    public static ExitRule VolumeSpike(int avgDays, decimal multiplier = 2.0m, int priority = 10) => new()
    {
        Type = ExitRuleType.VolumeSpike,
        Param1 = avgDays,
        Param2 = multiplier,
        Priority = priority,
        Evaluator = ctx =>
        {
            if (ctx.AvgVolume <= 0) return null;
            if (ctx.Today.Volume > (long)(ctx.AvgVolume * multiplier))
                return new ExitResult
                {
                    ExitPrice = ctx.Today.Close,
                    Reason = "volume_spike"
                };
            return null;
        }
    };

    // ========== 辅助计算函数 ==========

    /// <summary>计算 ATR(14)：过去 14 根日 K（含当前）真实波幅的均值。</summary>
    public static decimal CalcAtr14(IReadOnlyList<BacktestBar> bars, int endIdx)
    {
        if (bars.Count < 2 || endIdx < 1 || endIdx >= bars.Count) return 0m;
        var start = Math.Max(0, endIdx - 13);
        decimal sumTr = 0;
        for (int i = start + 1; i <= endIdx; i++)
        {
            var high = bars[i].High;
            var low = bars[i].Low;
            var prevClose = bars[i - 1].Close;
            var tr = Math.Max(high - low,
                Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            sumTr += tr;
        }
        var n = endIdx - start;
        return n > 0 ? sumTr / n : 0m;
    }

    /// <summary>计算 MA(period)：最近 period 根收盘的简单均值。</summary>
    public static decimal CalcMA(IReadOnlyList<BacktestBar> bars, int endIdx, int period)
    {
        if (bars.Count == 0 || endIdx < 0) return 0m;
        var start = Math.Max(0, endIdx - period + 1);
        var count = endIdx - start + 1;
        if (count < period) return 0m; // 数据不够不计算
        decimal sum = 0;
        for (int i = start; i <= endIdx; i++)
            sum += bars[i].Close;
        return sum / period;
    }

    /// <summary>计算 N 日均量。</summary>
    public static decimal CalcAvgVolume(IReadOnlyList<BacktestBar> bars, int endIdx, int period)
    {
        if (bars.Count == 0 || endIdx < 0) return 0m;
        var start = Math.Max(0, endIdx - period + 1);
        var count = endIdx - start + 1;
        if (count < period) return 0m;
        long sum = 0;
        for (int i = start; i <= endIdx; i++)
            sum += bars[i].Volume;
        return (decimal)sum / period;
    }
}
