using AIStock.Core.Models;
using AIStock.Selection.Performance;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 统一选股回测引擎（v2）。支持：
/// - 可组合出场规则引擎（止损/止盈/移动止损/ATR/MA/时间/放量）
/// - 向后兼容旧 StopLossPct / TakeProfitPct 字段
/// - 可组合入场规则（限价回调/量能确认/跳空限制）
/// - 组合层面资金约束（按时间线分配资金/持仓，现金守恒）
/// - 逐日权益曲线（DailyNav）+ 基准对比（沪深300等）
///
/// 仅服务 Selection 引擎（信号驱动）；Strategy 引擎走 <see cref="AIStock.Strategy.Services.BacktestEngineService"/>。
/// </summary>
public class BacktestEngine
{
    /// <summary>价格达到涨/跌停价 99.8% 即视为触板（容忍数据源精度误差）。</summary>
    private const decimal LimitTolerance = 0.998m;

    // ========== 主入口：Selection 回测 ==========

    /// <summary>
    /// 执行选股回测。给定信号列表 + 各股日K，按配置的进出场规则模拟成交。
    /// </summary>
    /// <param name="signals">选股信号列表（按日期排序）。</param>
    /// <param name="barsByCode">股票代码 → 日K线列表（升序）。</param>
    /// <param name="config">回测配置（统一版）。</param>
    /// <param name="benchmarkBars">可选：基准指数日K（升序），用于 Alpha/Beta/IR 计算。</param>
    public BacktestReport Run(
        IReadOnlyList<BacktestSignal> signals,
        IReadOnlyDictionary<string, List<BacktestBar>> barsByCode,
        BacktestConfig config,
        IReadOnlyList<BacktestBar>? benchmarkBars = null)
    {
        var holdDays = Math.Max(1, config.HoldDays);
        var friction = Math.Max(0m, config.FrictionPct);
        var skipped = 0;
        var skippedUntradable = 0;
        var skippedBreakdown = 0;

        // #4：出场规则只解析 + 按 Priority 排序一次（原先在最内层逐 bar 重排）
        var exitRules = ResolveExitRules(config).OrderBy(r => r.Priority).ToList();

        // #6：每个 code 的日K只排序一次（同一 code 多次出信号时复用），null 代表无数据
        var orderedCache = new Dictionary<string, List<BacktestBar>?>();
        List<BacktestBar>? GetOrdered(string code)
        {
            if (orderedCache.TryGetValue(code, out var cached)) return cached;
            List<BacktestBar>? ordered = barsByCode.TryGetValue(code, out var raw) && raw.Count > 0
                ? raw.OrderBy(b => b.Date).ToList()
                : null;
            orderedCache[code] = ordered;
            return ordered;
        }

        // ---- 阶段 A：逐信号模拟候选交易（不含资金分配）----
        var candidates = new List<CandidateTrade>();
        foreach (var sig in signals)
        {
            var ordered = GetOrdered(sig.Code);
            if (ordered == null) { skipped++; continue; }

            var cand = SimulateTrade(sig, ordered, config, exitRules, friction);
            switch (cand.Skip)
            {
                case SkipReason.NoData: skipped++; break;
                case SkipReason.Untradable: skippedUntradable++; break;
                case SkipReason.Breakdown: skippedBreakdown++; break;
                default: candidates.Add(cand); break;
            }
        }

        // ---- 阶段 B：资金分配 ----
        List<SelectionBacktestTrade> trades;
        List<DailyNav> equity;
        int skippedCapital;
        if (config.UsePortfolioConstraints)
        {
            (trades, equity, skippedCapital) = AllocatePortfolio(candidates, config);
        }
        else
        {
            trades = candidates.Select(c => c.Trade).ToList();
            equity = BuildEqualWeightEquity(candidates, config.InitialCapital);
            skippedCapital = 0;
        }

        var report = Summarize(trades, skipped, skippedCapital, signals.Count, holdDays, config.Entry);
        report.SkippedUntradable = skippedUntradable;
        report.SkippedBreakdown = skippedBreakdown;
        report.SkippedCapital = skippedCapital;
        report.DeferredExits = trades.Count(t => t.ExitDeferred);
        report.FrictionPct = friction;
        report.InitialCapital = config.InitialCapital;
        report.EquityCurve = equity;

        // 基于逐日净值计算年化 Sharpe
        if (equity.Count >= 3)
            report.SharpeRatio = Math.Round(ComputeSharpeFromDailyReturns(equity), 2);

        // 基准对比
        if (benchmarkBars is { Count: > 0 } && trades.Count > 0)
            ComputeBenchmarkMetrics(report, trades, benchmarkBars);

        // 统计检验
        if (trades.Count >= 5)
            ComputeStatisticalTests(report, trades);

        return report;
    }

    // ========== 阶段 A：单信号模拟 ==========

    /// <summary>
    /// 模拟单个信号的一笔候选交易（进场 → 出场规则遍历 → 出场）。
    /// 不做资金/持仓约束，仅产出"若买入则如何成交"的结果，供阶段 B 决定是否分配资金。
    /// </summary>
    private static CandidateTrade SimulateTrade(
        BacktestSignal sig, List<BacktestBar> ordered, BacktestConfig config,
        List<ExitRule> exitRules, decimal friction)
    {
        var holdDays = Math.Max(1, config.HoldDays);
        var sigIdx = ordered.FindIndex(b => b.Date.Date >= sig.Date.Date);
        if (sigIdx < 0) return CandidateTrade.NoData;

        var limitRatio = PerformanceCalculator.LimitUpRatio(sig.Code, sig.Name);

        // 破位否决：信号日(T)收盘 + 截至 T 的均线判定（无前视），命中则放弃该信号
        if (config.RejectBreakdown && IsBreakdown(ordered, sigIdx, config.Ma5DownSlopeMaxPct))
            return CandidateTrade.Breakdown;

        // ---- 入场：支持 EntryRules 或兼容旧 Entry 枚举 ----
        int entryIdx;
        decimal entryPrice;
        if (config.EntryRules is { Count: > 0 })
        {
            var entryResult = ResolveEntry(config.EntryRules, sigIdx, ordered, limitRatio);
            if (entryResult == null) return CandidateTrade.NoData;
            entryIdx = entryResult.EntryIdx;
            entryPrice = entryResult.EntryPrice;
        }
        else if (config.Entry == BacktestEntryTiming.SignalClose)
        {
            entryIdx = sigIdx;
            entryPrice = ordered[sigIdx].Close;
        }
        else
        {
            entryIdx = sigIdx + 1;
            if (entryIdx >= ordered.Count) return CandidateTrade.NoData;
            entryPrice = ordered[entryIdx].Open;
        }
        if (entryPrice <= 0) return CandidateTrade.NoData;

        // 可成交性：买入价已≈涨停（一字开盘/收盘封板）→ 买不进
        if (config.ApplyTradability && entryIdx > 0)
        {
            var prevClose = ordered[entryIdx - 1].Close;
            if (prevClose > 0 && entryPrice >= LimitPrice(prevClose, limitRatio) * LimitTolerance)
                return CandidateTrade.Untradable;
        }

        // #5：ATR(14) / 5 日均量只依赖 entryIdx，循环外算一次（原先逐 bar 重算）
        var atr14 = ExitRules.CalcAtr14(ordered, entryIdx);
        var avgVolume = ExitRules.CalcAvgVolume(ordered, Math.Max(0, entryIdx - 1), 5);

        // ---- 出场：规则引擎遍历 ----
        var maxExitIdx = Math.Min(entryIdx + holdDays * 2, ordered.Count - 1); // 放宽上限，由 TimeExit 兜底
        var exitIdx = entryIdx;
        var exitPrice = ordered[exitIdx].Close;
        var exitReason = "time";
        var exitDeferred = false;
        decimal highestHigh = entryPrice;
        decimal lowestLow = entryPrice;
        bool ruleTriggered = false;

        for (var i = entryIdx; i <= maxExitIdx; i++)
        {
            var bar = ordered[i];

            // 更新持仓期最高/最低
            if (bar.High > highestHigh) highestHigh = bar.High;
            if (bar.Low < lowestLow) lowestLow = bar.Low;

            var ctx = new ExitContext
            {
                EntryPrice = entryPrice,
                HighestHigh = highestHigh,
                LowestLow = lowestLow,
                Today = bar,
                PrevBar = i > entryIdx ? ordered[i - 1] : null,
                Atr14 = atr14,
                MaValue = 0m, // 由 MA 类规则按需计算
                AvgVolume = avgVolume,
                DaysHeld = i - entryIdx,
                LimitRatio = limitRatio,
            };

            // 一字跌停日不能执行止损类规则（除 TimeExit）
            var isOneWordDown = config.ApplyTradability && i > 0
                && IsLimitDownOneWord(bar, ordered[i - 1].Close, limitRatio);

            foreach (var rule in exitRules) // 已按 Priority 升序
            {
                if (rule.Evaluator == null) continue;

                // MA 类规则需要按需计算 MA 值
                if (rule.Type == ExitRuleType.MaCrossDown)
                {
                    ctx.MaValue = ExitRules.CalcMA(ordered, i, (int)rule.Param1);
                    if (ctx.MaValue <= 0) continue; // 数据不够则不触发
                }

                if (isOneWordDown && rule.Type is ExitRuleType.FixedStopLoss or ExitRuleType.TrailingStop
                        or ExitRuleType.AtrStop or ExitRuleType.TrailingTakeProfit)
                    continue;

                var result = rule.Evaluator(ctx);
                if (result != null)
                {
                    exitIdx = i;
                    exitPrice = result.ExitPrice;
                    exitReason = result.Reason;
                    ruleTriggered = true;
                    goto exitDone;
                }
            }
        }

        // 数据不足：持有期内未触发任何规则，退出到最后一根可用K线
        if (!ruleTriggered)
        {
            exitIdx = maxExitIdx;
            exitReason = "insufficient_data";
        }

    exitDone:

        // 可成交性：到期卖出日一字跌停 → 顺延
        if (config.ApplyTradability && exitReason == "time")
        {
            while (exitIdx < ordered.Count - 1 && exitIdx > 0
                && IsLimitDownOneWord(ordered[exitIdx], ordered[exitIdx - 1].Close, limitRatio))
            {
                exitIdx++;
                exitDeferred = true;
            }
        }

        var exitBar = ordered[exitIdx];
        if (exitReason == "time" || !ruleTriggered)
            exitPrice = exitBar.Close;

        var span = ordered.GetRange(entryIdx, exitIdx - entryIdx + 1);

        decimal Pct(decimal price) => Math.Round((price - entryPrice) / entryPrice * 100m, 2);

        var trade = new SelectionBacktestTrade
        {
            Code = sig.Code,
            Name = sig.Name,
            SignalDate = sig.Date.Date,
            EntryDate = ordered[entryIdx].Date,
            EntryPrice = entryPrice,
            ExitDate = exitBar.Date,
            ExitPrice = exitPrice,
            HoldDays = exitIdx - entryIdx,
            ReturnPct = Pct(exitPrice) - friction,
            ExitDeferred = exitDeferred,
            ExitReason = exitReason,
            MaxRisePct = Pct(span.Max(b => b.High)),
            MaxDropPct = Pct(span.Min(b => b.Low)),
        };

        // 持仓期逐日收盘路径（供等权权益曲线 / 组合 MTM）
        var path = new List<(DateTime Date, decimal Close)>(span.Count);
        for (int j = entryIdx; j <= exitIdx; j++)
            path.Add((ordered[j].Date, ordered[j].Close));

        return new CandidateTrade { Skip = SkipReason.None, Trade = trade, Score = sig.Score, Path = path };
    }

    // ========== 阶段 B：权益曲线 / 资金分配 ==========

    /// <summary>
    /// 非组合模式：等权日收益。把每笔交易持仓期的逐日收益按日期等权平均得到组合日收益，
    /// 复利出权益曲线。每个持仓日（含入场日，入场日记 0）贡献一个数据点。
    /// </summary>
    private static List<DailyNav> BuildEqualWeightEquity(List<CandidateTrade> candidates, decimal initialCapital)
    {
        var byDate = new SortedDictionary<DateTime, (decimal Sum, int N)>();
        foreach (var c in candidates)
        {
            var path = c.Path;
            for (int i = 0; i < path.Count; i++)
            {
                var date = path[i].Date.Date;
                var ret = i == 0 || path[i - 1].Close == 0
                    ? 0m
                    : (path[i].Close / path[i - 1].Close - 1) * 100m;
                if (byDate.TryGetValue(date, out var agg))
                    byDate[date] = (agg.Sum + ret, agg.N + 1);
                else
                    byDate[date] = (ret, 1);
            }
        }

        var equity = new List<DailyNav>(byDate.Count);
        var nav = initialCapital > 0 ? initialCapital : 100m;
        foreach (var kv in byDate)
        {
            var r = kv.Value.N > 0 ? kv.Value.Sum / kv.Value.N : 0m;
            nav *= 1 + r / 100m;
            equity.Add(new DailyNav { Date = kv.Key, Nav = Math.Round(nav, 2), Return = Math.Round(r, 4) });
        }
        return equity;
    }

    /// <summary>
    /// 组合模式：按时间线推进逐日模拟。每个交易日先平仓（到期/触发出场），再按打分降序开仓，
    /// 受 MaxPositions（持仓数）与 MaxPositionPercent（单仓占比）约束，现金严格守恒。
    /// 返回（已成交交易, 逐日权益, 因资金/仓位约束放弃的笔数）。
    /// </summary>
    private static (List<SelectionBacktestTrade> Trades, List<DailyNav> Equity, int SkippedCapital)
        AllocatePortfolio(List<CandidateTrade> candidates, BacktestConfig config)
    {
        var initial = config.MaxCapital ?? config.InitialCapital;
        if (initial <= 0) initial = 1_000_000m;
        var maxPositions = config.MaxPositions > 0 ? config.MaxPositions : int.MaxValue;
        var posPct = config.MaxPositionPercent > 0 ? config.MaxPositionPercent : 100m;

        var entriesByDate = candidates
            .GroupBy(c => c.Trade.EntryDate.Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Score).ToList());

        var allDates = candidates
            .SelectMany(c => new[] { c.Trade.EntryDate.Date, c.Trade.ExitDate.Date })
            .Distinct().OrderBy(d => d).ToList();

        var cash = initial;
        var open = new Dictionary<string, (CandidateTrade Cand, long Lots)>();
        var realized = new List<SelectionBacktestTrade>();
        var equity = new List<DailyNav>(allDates.Count);
        var skippedCapital = 0;
        var prevEquity = initial;

        foreach (var date in allDates)
        {
            // 1) 先平仓（出场日 == 当日）
            foreach (var code in open.Keys.ToList())
            {
                var (cand, lots) = open[code];
                if (cand.Trade.ExitDate.Date != date) continue;
                cash += lots * cand.Trade.ExitPrice;
                open.Remove(code);
                realized.Add(cand.Trade);
            }

            // 2) 再开仓（入场日 == 当日），按打分降序优先
            if (entriesByDate.TryGetValue(date, out var todays))
            {
                foreach (var cand in todays)
                {
                    if (open.Count >= maxPositions) { skippedCapital++; continue; }
                    if (open.ContainsKey(cand.Trade.Code)) { skippedCapital++; continue; }

                    var entryPrice = cand.Trade.EntryPrice;
                    var budget = cash * posPct / 100m;
                    var lots = (long)(budget / entryPrice / 100m) * 100;
                    if (lots <= 0) { skippedCapital++; continue; }
                    var cost = lots * entryPrice;
                    if (cost > cash) { skippedCapital++; continue; }

                    cash -= cost;
                    open[cand.Trade.Code] = (cand, lots);
                }
            }

            // 3) 当日权益 = 现金 + 持仓市值（按持仓期内截至当日收盘 MTM）
            decimal mtm = 0m;
            foreach (var (cand, lots) in open.Values)
                mtm += lots * CloseOn(cand, date);
            var eq = cash + mtm;

            equity.Add(new DailyNav
            {
                Date = date,
                Nav = Math.Round(eq, 2),
                Return = prevEquity != 0 ? Math.Round((eq - prevEquity) / prevEquity * 100m, 4) : 0m,
            });
            prevEquity = eq;
        }

        return (realized, equity, skippedCapital);
    }

    /// <summary>
    /// 破位判定（无前视，用信号日 T 收盘 + 截至 T 的均线）：
    /// 收盘跌破 MA10，或 MA5 较前一日下跌超过阈值（拐头向下斜率太大），任一成立即破位。
    /// 均线数据不足（CalcMA 返回 0）时该子条件不参与判定（不误杀）。
    /// </summary>
    private static bool IsBreakdown(List<BacktestBar> ordered, int sigIdx, decimal ma5DownSlopeMaxPct)
    {
        var close = ordered[sigIdx].Close;

        // 收盘跌破 MA10
        var ma10 = ExitRules.CalcMA(ordered, sigIdx, 10);
        if (ma10 > 0 && close < ma10) return true;

        // MA5 拐头向下且斜率过大
        if (sigIdx >= 1)
        {
            var ma5Now = ExitRules.CalcMA(ordered, sigIdx, 5);
            var ma5Prev = ExitRules.CalcMA(ordered, sigIdx - 1, 5);
            if (ma5Prev > 0 && ma5Now > 0)
            {
                var dropPct = (ma5Prev - ma5Now) / ma5Prev * 100m;
                if (dropPct > ma5DownSlopeMaxPct) return true;
            }
        }
        return false;
    }

    /// <summary>取持仓在某日的收盘价（path 升序，返回 ≤ date 的最后一个收盘）。</summary>
    private static decimal CloseOn(CandidateTrade cand, DateTime date)
    {
        var last = cand.Trade.EntryPrice;
        foreach (var (d, close) in cand.Path)
        {
            if (d.Date <= date) last = close;
            else break;
        }
        return last;
    }

    // ========== 候选交易类型 ==========

    private enum SkipReason { None, NoData, Untradable, Breakdown }

    private sealed class CandidateTrade
    {
        public SkipReason Skip;
        public SelectionBacktestTrade Trade = null!;
        public decimal Score;
        public List<(DateTime Date, decimal Close)> Path = new();

        public static readonly CandidateTrade NoData = new() { Skip = SkipReason.NoData };
        public static readonly CandidateTrade Untradable = new() { Skip = SkipReason.Untradable };
        public static readonly CandidateTrade Breakdown = new() { Skip = SkipReason.Breakdown };
    }

    // ========== 出场规则解析（向后兼容） ==========

    /// <summary>
    /// 解析出场规则：优先 ExitPreset → 显式 ExitRules → 旧 StopLossPct/TakeProfitPct 字段（向后兼容），
    /// 兜底至少包含一条 TimeExit。
    /// </summary>
    private static List<ExitRule> ResolveExitRules(BacktestConfig config)
    {
        // 1. 预设快捷方式
        if (!string.IsNullOrWhiteSpace(config.ExitPreset))
        {
            var preset = ExitRulePresets.Resolve(config.ExitPreset);
            if (preset != null) return preset;
        }

        // 2. 显式 ExitRules
        if (config.ExitRules is { Count: > 0 })
            return config.ExitRules;

        // 3. 向后兼容：旧字段
        var rules = new List<ExitRule>();
        if (config.StopLossPct > 0)
            rules.Add(ExitRules.FixedStopLoss(config.StopLossPct, priority: 1));
        if (config.TakeProfitPct > 0)
            rules.Add(ExitRules.FixedTakeProfit(config.TakeProfitPct, priority: 2));

        // 4. 兜底：TimeExit
        rules.Add(ExitRules.TimeExit(config.HoldDays, priority: 99));
        return rules;
    }

    // ========== 入场规则解析 ==========

    private static EntryResult? ResolveEntry(
        List<EntryRule> entryRules, int sigIdx, List<BacktestBar> ordered, decimal limitRatio)
    {
        foreach (var rule in entryRules)
        {
            switch (rule.Type)
            {
                case EntryRuleType.NextOpen:
                    var idx = sigIdx + 1;
                    if (idx >= ordered.Count) continue;
                    return new EntryResult { EntryIdx = idx, EntryPrice = ordered[idx].Open };

                case EntryRuleType.SignalClose:
                    return new EntryResult { EntryIdx = sigIdx, EntryPrice = ordered[sigIdx].Close };

                case EntryRuleType.GapLimit:
                    // 跳空限制：T+1 开盘价 > 信号日收盘 × (1+Param1%) → 放弃
                    var t1Idx = sigIdx + 1;
                    if (t1Idx >= ordered.Count) continue;
                    var gapLimit = ordered[sigIdx].Close * (1 + rule.Param1 / 100m);
                    if (ordered[t1Idx].Open > gapLimit) return null; // 放弃
                    return new EntryResult { EntryIdx = t1Idx, EntryPrice = ordered[t1Idx].Open };

                case EntryRuleType.VolumeConfirm:
                    // 量能确认：T+1 成交量 > 信号日 × Param1 倍
                    var vIdx = sigIdx + 1;
                    if (vIdx >= ordered.Count) continue;
                    if (ordered[vIdx].Volume < (long)(ordered[sigIdx].Volume * rule.Param1)) return null;
                    return new EntryResult { EntryIdx = vIdx, EntryPrice = ordered[vIdx].Open };

                case EntryRuleType.LimitPullback:
                    // 限价回调：信号后 MaxWaitDays 日内，最低价回调到 Param1%
                    var targetPrice = ordered[sigIdx].Close * (1 - rule.Param1 / 100m);
                    for (int i = sigIdx + 1; i <= sigIdx + rule.MaxWaitDays && i < ordered.Count; i++)
                    {
                        if (ordered[i].Low <= targetPrice)
                            return new EntryResult
                            {
                                EntryIdx = i,
                                EntryPrice = Math.Min(ordered[i].Open, targetPrice)
                            };
                    }
                    return null; // 未回调到目标价，放弃
            }
        }
        return null;
    }

    // ========== 辅助函数 ==========

    private static decimal LimitPrice(decimal prevClose, decimal ratio)
        => Math.Round(prevClose * (1 + ratio), 2);

    private static bool IsLimitDownOneWord(BacktestBar bar, decimal prevClose, decimal ratio)
    {
        if (prevClose <= 0 || bar.High != bar.Low) return false;
        var limitDown = Math.Round(prevClose * (1 - ratio), 2);
        return bar.Close <= limitDown / LimitTolerance;
    }

    // ========== 汇总 ==========

    private static BacktestReport Summarize(
        List<SelectionBacktestTrade> trades, int skipped, int skippedCapital,
        int totalSignals, int holdDays, BacktestEntryTiming entry)
    {
        var report = new BacktestReport
        {
            HoldDays = holdDays,
            Entry = entry.ToString(),
            TotalSignals = totalSignals,
            ExecutedTrades = trades.Count,
            SkippedNoData = skipped,
            SkippedCapital = skippedCapital,
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
            report.ProfitFactor = null;
        }

        report.StdDevPct = Math.Round(StdDev(returns), 2);
        report.MaxDrawdownPct = Math.Round(MaxDrawdown(trades), 2);

        return report;
    }

    // ========== 基准对比 ==========

    private static void ComputeBenchmarkMetrics(
        BacktestReport report, List<SelectionBacktestTrade> trades, IReadOnlyList<BacktestBar> benchmarkBars)
    {
        var benchByDate = benchmarkBars
            .GroupBy(b => b.Date.Date)
            .ToDictionary(g => g.Key, g => g.First().Close);

        // 计算每笔交易区间的基准收益
        var benchReturns = new List<decimal>();
        foreach (var t in trades)
        {
            if (benchByDate.TryGetValue(t.EntryDate.Date, out var bEntry) && bEntry > 0
                && benchByDate.TryGetValue(t.ExitDate.Date, out var bExit) && bExit > 0)
            {
                benchReturns.Add((bExit / bEntry - 1) * 100m);
            }
        }

        if (benchReturns.Count == 0) return;
        report.BenchmarkReturn = Math.Round(benchReturns.Average(), 2);

        // Alpha = 平均超额
        var excessReturns = trades.Zip(benchReturns, (t, b) => t.ReturnPct - b).ToList();
        if (excessReturns.Count > 0)
            report.Alpha = Math.Round(excessReturns.Average(), 2);

        // Beta = Cov(策略,基准) / Var(基准)
        if (benchReturns.Count >= 3)
        {
            var strategyReturns = trades.Take(benchReturns.Count).Select(t => t.ReturnPct).ToList();
            report.Beta = Math.Round(CalcBeta(strategyReturns, benchReturns), 2);

            // IR = Alpha / StdDev(超额)
            var excessStd = StdDev(excessReturns);
            if (excessStd > 0)
                report.InformationRatio = Math.Round((report.Alpha ?? 0) / excessStd, 2);
        }
    }

    private static decimal CalcBeta(List<decimal> strategy, List<decimal> benchmark)
    {
        var n = Math.Min(strategy.Count, benchmark.Count);
        if (n < 3) return 0;
        var sAvg = strategy.Take(n).Average();
        var bAvg = benchmark.Take(n).Average();
        decimal cov = 0, bVar = 0;
        for (int i = 0; i < n; i++)
        {
            cov += (strategy[i] - sAvg) * (benchmark[i] - bAvg);
            bVar += (benchmark[i] - bAvg) * (benchmark[i] - bAvg);
        }
        return bVar != 0 ? cov / bVar : 0;
    }

    // ========== 统计检验 ==========

    private static void ComputeStatisticalTests(BacktestReport report, List<SelectionBacktestTrade> trades)
    {
        var returns = trades.Select(t => (double)t.ReturnPct).ToList();
        var n = returns.Count;
        var mean = returns.Average();
        var std = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (n - 1));
        var se = std / Math.Sqrt(n);

        // t 检验 H0: 均值=0
        if (se > 0)
        {
            var tStat = mean / se;
            // 近似 p-value（双尾，df=n-1）
            var pValue = 2 * (1 - TDistCdf(Math.Abs(tStat), n - 1));
            report.AvgReturnPValue = Math.Round((decimal)pValue, 4);
        }

        // Bootstrap 95% CI for WinRate
        var rng = new Random(42);
        var bootstraps = new List<double>();
        for (int b = 0; b < 1000; b++)
        {
            var wins = 0;
            for (int i = 0; i < n; i++)
                if (returns[rng.Next(n)] > 0) wins++;
            bootstraps.Add((double)wins / n);
        }
        bootstraps.Sort();
        report.WinRateCI_Lower = Math.Round((decimal)bootstraps[25] * 100m, 1);
        report.WinRateCI_Upper = Math.Round((decimal)bootstraps[974] * 100m, 1);
    }

    /// <summary>Student's t 分布 CDF 近似（基于正则化不完全 Beta）。</summary>
    private static double TDistCdf(double t, int df)
    {
        if (df <= 0) return 0.5;
        var x = df / (df + t * t);
        // I_x(df/2, 1/2) = P(|T| > t)；CDF(t) = 1 - 0.5 * I_x（t>=0）
        var ib = RegIncompleteBeta(df / 2.0, 0.5, x);
        return 1.0 - 0.5 * ib;
    }

    /// <summary>正则化不完全 Beta I_x(a,b)，含连分数收敛域的对称变换。</summary>
    private static double RegIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0) return 0.0;
        if (x >= 1) return 1.0;

        // 前置因子 x^a (1-x)^b / B(a,b)
        var lnFront = a * Math.Log(x) + b * Math.Log(1 - x) - LogBeta(a, b);
        var front = Math.Exp(lnFront);

        // 连分数在 x < (a+1)/(a+b+2) 收敛快；否则用对称式 I_x(a,b) = 1 - I_{1-x}(b,a)
        if (x < (a + 1) / (a + b + 2))
            return front * BetaContinuedFraction(a, b, x) / a;
        return 1.0 - front * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    /// <summary>不完全 Beta 连分数（Lentz 法）。</summary>
    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const int maxIter = 200;
        const double eps = 1e-15;
        const double fpMin = 1e-300;

        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1.0 - qab * x / qap;
        if (Math.Abs(d) < fpMin) d = fpMin;
        d = 1.0 / d;
        var h = d;

        for (int m = 1; m <= maxIter; m++)
        {
            var m2 = 2 * m;
            // even step
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < fpMin) d = fpMin;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < fpMin) c = fpMin;
            d = 1.0 / d;
            h *= d * c;
            // odd step
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < fpMin) d = fpMin;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < fpMin) c = fpMin;
            d = 1.0 / d;
            var del = d * c;
            h *= del;
            if (Math.Abs(del - 1.0) < eps) break;
        }
        return h;
    }

    private static double LogBeta(double a, double b)
        => LnGamma(a) + LnGamma(b) - LnGamma(a + b);

    private static double LnGamma(double x)
    {
        // Lanczos 近似
        if (x <= 0) return 0;
        var v = x;
        var s = 1.0 + 76.18009173 / v - 86.50532033 / (v + 1) + 24.01409822 / (v + 2)
            - 1.231739516 / (v + 3) + 0.00120858003 / (v + 4) - 0.00000536382 / (v + 5);
        return Math.Log(2.50662827465 * s) + (v + 0.5) * Math.Log(v + 4.5) - (v + 4.5);
    }

    // ========== 纯数学工具 ==========

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

    /// <summary>按信号时间排序的累计收益曲线最大回撤（百分点）。</summary>
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

    /// <summary>基于逐日权益曲线的日收益计算年化 Sharpe 比率。</summary>
    private static decimal ComputeSharpeFromDailyReturns(List<DailyNav> equity)
    {
        var dailyReturns = equity.Select(n => n.Return).ToList();
        if (dailyReturns.Count < 2) return 0m;

        var avgReturn = dailyReturns.Average();
        var stdDaily = StdDev(dailyReturns);
        if (stdDaily == 0) return 0m;

        // 年化：日收益均值 / 日收益标准差 × sqrt(252)
        return avgReturn / stdDaily * (decimal)Math.Sqrt(252);
    }

    // ========== 单笔回测便捷方法（向前兼容） ==========

    /// <summary>兼容旧代码的静态入口。</summary>
    [Obsolete("Use new BacktestEngine().Run() instead.")]
    public static BacktestReport Run(
        IReadOnlyList<BacktestSignal> signals,
        IReadOnlyDictionary<string, List<BacktestBar>> barsByCode,
        BacktestConfig legacyConfig)
    {
        var engine = new BacktestEngine();
        return engine.Run(signals, barsByCode, legacyConfig);
    }
}
