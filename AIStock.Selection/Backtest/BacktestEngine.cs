using AIStock.Selection.Performance;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 统一回测引擎（纯计算）。给定一批选股信号 + 各股日 K，按"T+1 开盘买入、持有 N 个交易日收盘卖出"
/// 模拟成交并汇总：胜率 / 平均收益 / 盈亏比 / 最大回撤 / 浮盈浮亏。不依赖快照历史完整性，便于严格单测。
/// 可成交性约束（默认开）：买入价已≈涨停 → 买不进剔除；卖出日一字跌停 → 顺延到可卖日；
/// 每笔收益扣除往返摩擦（佣金+印花税+滑点）。涨跌停幅度按板块/ST 推断（同绩效归因口径）。
/// </summary>
public static class BacktestEngine
{
    /// <summary>价格达到涨/跌停价 99.8% 即视为触板（容忍数据源精度误差）。</summary>
    private const decimal LimitTolerance = 0.998m;

    public static BacktestReport Run(
        IReadOnlyList<BacktestSignal> signals,
        IReadOnlyDictionary<string, List<BacktestBar>> barsByCode,
        BacktestConfig config)
    {
        var holdDays = Math.Max(1, config.HoldDays);
        var friction = Math.Max(0m, config.FrictionPct);
        var trades = new List<SelectionBacktestTrade>();
        var skipped = 0;
        var skippedUntradable = 0;
        var deferredExits = 0;

        foreach (var sig in signals)
        {
            if (!barsByCode.TryGetValue(sig.Code, out var bars) || bars.Count == 0)
            {
                skipped++;
                continue;
            }

            // 升序保证 + 定位信号日（取信号日当天或之后的第一根，容忍信号日停牌）
            var ordered = bars.OrderBy(b => b.Date).ToList();
            var sigIdx = ordered.FindIndex(b => b.Date.Date >= sig.Date.Date);
            if (sigIdx < 0)
            {
                skipped++;
                continue;
            }

            var limitRatio = PerformanceCalculator.LimitUpRatio(sig.Code, sig.Name);

            // 买入：T+1 开盘（默认）或信号日收盘
            int entryIdx;
            decimal entryPrice;
            if (config.Entry == BacktestEntryTiming.SignalClose)
            {
                entryIdx = sigIdx;
                entryPrice = ordered[sigIdx].Close;
            }
            else
            {
                entryIdx = sigIdx + 1;
                if (entryIdx >= ordered.Count) { skipped++; continue; } // 无次日数据
                entryPrice = ordered[entryIdx].Open;
            }
            if (entryPrice <= 0) { skipped++; continue; }

            // 可成交性：买入价已≈涨停（一字开盘 / 收盘封板）→ 买不进
            if (config.ApplyTradability && entryIdx > 0)
            {
                var prevClose = ordered[entryIdx - 1].Close;
                if (prevClose > 0 && entryPrice >= LimitPrice(prevClose, limitRatio) * LimitTolerance)
                {
                    skippedUntradable++;
                    continue;
                }
            }

            // 卖出：持有 holdDays 个交易日后的收盘；不足则持有到最后一根
            var exitIdx = Math.Min(entryIdx + holdDays, ordered.Count - 1);
            if (exitIdx <= entryIdx && config.Entry == BacktestEntryTiming.NextOpen)
            {
                // 买入即末根，无持有区间
                exitIdx = entryIdx;
            }

            // 可成交性：卖出日一字跌停（最高=最低≈跌停价）→ 顺延到下一个可卖日；
            // 顺延到最后一根仍跌停则按该根收盘成交（无法再延，保守接受）
            var exitDeferred = false;
            if (config.ApplyTradability)
            {
                while (exitIdx < ordered.Count - 1 && exitIdx > 0
                    && IsLimitDownOneWord(ordered[exitIdx], ordered[exitIdx - 1].Close, limitRatio))
                {
                    exitIdx++;
                    exitDeferred = true;
                }
                if (exitDeferred) deferredExits++;
            }

            var exitBar = ordered[exitIdx];
            var span = ordered.GetRange(entryIdx, exitIdx - entryIdx + 1);

            decimal Pct(decimal price) => Math.Round((price - entryPrice) / entryPrice * 100m, 2);

            trades.Add(new SelectionBacktestTrade
            {
                Code = sig.Code,
                Name = sig.Name,
                SignalDate = sig.Date.Date,
                EntryDate = ordered[entryIdx].Date,
                EntryPrice = entryPrice,
                ExitDate = exitBar.Date,
                ExitPrice = exitBar.Close,
                HoldDays = exitIdx - entryIdx,
                ReturnPct = Pct(exitBar.Close) - friction,
                ExitDeferred = exitDeferred,
                MaxRisePct = Pct(span.Max(b => b.High)),
                MaxDropPct = Pct(span.Min(b => b.Low)),
            });
        }

        var report = Summarize(trades, skipped, signals.Count, holdDays, config.Entry);
        report.SkippedUntradable = skippedUntradable;
        report.DeferredExits = deferredExits;
        report.FrictionPct = friction;
        return report;
    }

    /// <summary>涨停价（昨收 × (1+幅度)，A股两位小数四舍五入）。</summary>
    private static decimal LimitPrice(decimal prevClose, decimal ratio)
        => Math.Round(prevClose * (1 + ratio), 2);

    /// <summary>一字跌停：最高=最低 且 收盘 ≤ 跌停价附近 → 全天封死卖不出。</summary>
    private static bool IsLimitDownOneWord(BacktestBar bar, decimal prevClose, decimal ratio)
    {
        if (prevClose <= 0 || bar.High != bar.Low) return false;
        var limitDown = Math.Round(prevClose * (1 - ratio), 2);
        return bar.Close <= limitDown / LimitTolerance;
    }

    private static BacktestReport Summarize(
        List<SelectionBacktestTrade> trades, int skipped, int totalSignals, int holdDays, BacktestEntryTiming entry)
    {
        var report = new BacktestReport
        {
            HoldDays = holdDays,
            Entry = entry.ToString(),
            TotalSignals = totalSignals,
            ExecutedTrades = trades.Count,
            SkippedNoData = skipped,
            Trades = trades,
        };
        if (trades.Count == 0) return report;

        var returns = trades.Select(t => t.ReturnPct).ToList();
        var wins = returns.Where(r => r > 0).ToList();
        var losses = returns.Where(r => r <= 0).ToList();

        report.WinRatePct = Math.Round((decimal)wins.Count / trades.Count * 100m, 1);
        report.AvgReturnPct = Math.Round(returns.Average(), 2);
        report.MedianReturnPct = Math.Round(Median(returns), 2);
        report.BestReturnPct = returns.Max();
        report.WorstReturnPct = returns.Min();
        report.AvgMaxRisePct = Math.Round(trades.Average(t => t.MaxRisePct), 2);
        report.AvgMaxDropPct = Math.Round(trades.Average(t => t.MaxDropPct), 2);

        if (losses.Count > 0 && losses.Sum() != 0)
        {
            var avgWin = wins.Count > 0 ? wins.Average() : 0m;
            var avgLoss = Math.Abs(losses.Average());
            report.ProfitFactor = avgLoss == 0 ? null : Math.Round(avgWin / avgLoss, 2);
        }
        else
        {
            report.ProfitFactor = null; // 无亏损样本
        }

        report.StdDevPct = Math.Round(StdDev(returns), 2);
        report.MaxDrawdownPct = Math.Round(MaxDrawdown(trades), 2);

        return report;
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// 按信号时间排序的累计收益曲线（等权算术累加）最大回撤（百分点）。
    /// 反映"按时间顺序持续按该策略操作"的资金曲线最深回撤。
    /// </summary>
    private static decimal MaxDrawdown(List<SelectionBacktestTrade> trades)
    {
        var ordered = trades.OrderBy(t => t.EntryDate).ToList();
        decimal cum = 0m, peak = 0m, maxDd = 0m;
        foreach (var t in ordered)
        {
            cum += t.ReturnPct;
            peak = Math.Max(peak, cum);
            maxDd = Math.Max(maxDd, peak - cum);
        }
        return maxDd;
    }
}
