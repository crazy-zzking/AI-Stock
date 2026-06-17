using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Strategy.Services;

/// <summary>
/// 回测引擎实现 — Strategy 引擎（多股票策略回测）。
/// 
/// v1.1 修复：
/// - [FIX] capital 不再在 foreach(code) 内重置（#3）
/// - [FIX] Sharpe 从"逐笔收益×√252"改为"逐日净值→日收益率→mean/std×√252"（#4）
/// - [FIX] MaxDrawdown 改为逐日 Mark-to-Market 计算（#5）
/// - [PERF] 历史前缀随日期只追加，去掉逐日全历史 Where+OrderBy+ToList 重排
/// - [NOTE] GenerateSignalAsync 可能非确定性，建议回测走纯规则信号（#6）
/// </summary>
public class BacktestEngineService : IBacktestEngine
{
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<BacktestEngineService> _logger;

    public BacktestEngineService(
        IFeatureCalculator featureCalculator,
        IDataProviderResolver dataProviderResolver,
        ILogger<BacktestEngineService> logger)
    {
        _featureCalculator = featureCalculator;
        _dataProviderResolver = dataProviderResolver;
        _logger = logger;
    }

    public async Task<BacktestResult> RunBacktestAsync(
        BacktestConfig config, IStrategy strategy, List<string> codes,
        DateTime startTime, DateTime endTime)
    {
        var result = new BacktestResult
        {
            StrategyName = strategy.Name,
            StartTime = startTime,
            EndTime = endTime,
            InitialCapital = config.InitialCapital
        };

        // ★ FIX #3: capital 移到 foreach 外层，所有股票共享同一资金
        var capital = config.InitialCapital;
        var positions = new Dictionary<string, BacktestTrade>();
        var trades = new List<BacktestTrade>();
        var allDailyNavs = new List<DailyNav>();

        // 先加载所有股票的 K 线，构建全量时间线
        var allKlines = new Dictionary<string, List<KlineData>>();
        foreach (var code in codes)
        {
            try
            {
                var klines = await GetKlinesAsync(code, startTime, endTime);
                if (klines != null && klines.Count >= 60)
                    allKlines[code] = klines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading klines for {Code}", code);
            }
        }

        if (allKlines.Count == 0) return result;

        // 构建统一时间线（所有股票的交易日期并集）
        var allDates = allKlines.Values
            .SelectMany(k => k.Select(x => x.DateTime.Date))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // 为每个股票建立日期→K线索引（用于取当日成交价 / MTM）
        var klineByCodeAndDate = new Dictionary<string, Dictionary<DateTime, KlineData>>();
        foreach (var (code, klines) in allKlines)
        {
            var dict = new Dictionary<DateTime, KlineData>(klines.Count);
            foreach (var k in klines)
                dict[k.DateTime.Date] = k;
            klineByCodeAndDate[code] = dict;
        }

        // 按日期归集当日有K线的 (code, kline)，供逐日把K线追加进各股"历史前缀"。
        // 避免每个交易日对全历史做 Where+OrderBy+ToList（原 O(天²) 重排，现 O(1) 追加）。
        var klinesByDate = new Dictionary<DateTime, List<(string Code, KlineData Kline)>>();
        foreach (var (code, klines) in allKlines)
            foreach (var k in klines)
            {
                var d = k.DateTime.Date;
                if (!klinesByDate.TryGetValue(d, out var list))
                    klinesByDate[d] = list = new List<(string, KlineData)>();
                list.Add((code, k));
            }

        // 各股"截至当日"的历史前缀（升序）：随 date 推进只追加，不重排不复制。
        var historyByCode = new Dictionary<string, List<KlineData>>(allKlines.Count);
        foreach (var (code, klines) in allKlines)
            historyByCode[code] = new List<KlineData>(klines.Count);

        // ★ 逐日模拟（修复 #5：逐日 Mark-to-Market）
        foreach (var date in allDates)
        {
            // 把当日K线追加进各股历史前缀（升序保证：allDates 升序 + 每股K线升序）
            if (klinesByDate.TryGetValue(date, out var todayKlines))
                foreach (var (code, k) in todayKlines)
                    historyByCode[code].Add(k);

            // 1. 先执行今日卖出信号（从 pending_sells 或今日信号）
            var codesToSell = new List<string>();
            foreach (var (code, pos) in positions)
            {
                if (!klineByCodeAndDate.TryGetValue(code, out var dateMap)
                    || !dateMap.ContainsKey(date))
                    continue;

                var historyKlines = historyByCode[code];
                if (historyKlines.Count < 60) continue;

                var indicators = _featureCalculator.CalculateAll(code, historyKlines);
                var signal = await strategy.GenerateSignalAsync(code, historyKlines, indicators);

                if (signal is { SignalType: SignalType.Sell or SignalType.StopLoss })
                {
                    codesToSell.Add(code);
                }
            }

            foreach (var code in codesToSell)
            {
                if (!positions.TryGetValue(code, out var position)) continue;
                if (!klineByCodeAndDate.TryGetValue(code, out var dateMap)
                    || !dateMap.TryGetValue(date, out var currentKline))
                    continue;

                var sellPrice = currentKline.Close;
                var sellAmount = position.Volume * sellPrice;
                var commission = sellAmount * config.CommissionRate / 100;
                var stampTax = sellAmount * config.StampTaxRate / 100;
                var slippage = sellAmount * config.Slippage / 100;
                var impact = sellAmount * config.ImpactCost / 100;
                var netAmount = sellAmount - commission - stampTax - slippage - impact;

                capital += netAmount;

                position.SellTime = currentKline.DateTime;
                position.SellPrice = sellPrice;
                position.Profit = netAmount - position.Volume * position.BuyPrice;
                position.ProfitRate = position.Profit / (position.Volume * position.BuyPrice) * 100;
                position.Commission += commission + stampTax + slippage + impact;

                trades.Add(position);
                positions.Remove(code);
            }

            // 2. 再执行今日买入信号
            foreach (var code in allKlines.Keys)
            {
                if (positions.ContainsKey(code)) continue; // 已持仓
                if (positions.Count >= config.MaxPositions) break; // 已达持仓上限

                if (!klineByCodeAndDate.TryGetValue(code, out var dateMap)
                    || !dateMap.TryGetValue(date, out var currentKline))
                    continue;

                var historyKlines = historyByCode[code];
                if (historyKlines.Count < 60) continue;

                var indicators = _featureCalculator.CalculateAll(code, historyKlines);
                var signal = await strategy.GenerateSignalAsync(code, historyKlines, indicators);

                if (signal is not { SignalType: SignalType.Buy }) continue;

                var maxAmount = capital * config.MaxPositionPercent / 100;
                var volume = (long)(maxAmount / currentKline.Close / 100) * 100;
                if (volume <= 0) continue;

                var cost = volume * currentKline.Close;
                var commission = cost * config.CommissionRate / 100;
                var slippage = cost * config.Slippage / 100;
                var impact = cost * config.ImpactCost / 100;
                var totalCost = cost + commission + slippage + impact;

                if (totalCost > capital) continue;

                capital -= totalCost;

                positions[code] = new BacktestTrade
                {
                    Code = code,
                    BuyTime = currentKline.DateTime,
                    BuyPrice = currentKline.Close,
                    Volume = volume,
                    Commission = commission + slippage + impact
                };
            }

            // 3. ★ 记录今日权益（MTM，含持仓市值）— 修复 #5
            var positionsValue = positions.Values.Sum(p =>
            {
                if (!klineByCodeAndDate.TryGetValue(p.Code, out var dm) || !dm.TryGetValue(date, out var k))
                    return p.Volume * p.BuyPrice; // fallback to cost
                return p.Volume * k.Close;
            });
            var totalEquity = capital + positionsValue;

            allDailyNavs.Add(new DailyNav
            {
                Date = date,
                Nav = totalEquity,
                Return = allDailyNavs.Count > 0 && allDailyNavs[^1].Nav > 0
                    ? (totalEquity - allDailyNavs[^1].Nav) / allDailyNavs[^1].Nav * 100
                    : 0,
            });
        }

        // 强制平仓：末日期末清仓
        foreach (var position in positions.Values)
        {
            if (klineByCodeAndDate.TryGetValue(position.Code, out var dm)
                && dm.TryGetValue(allDates[^1], out var lastKline))
            {
                position.SellTime = lastKline.DateTime;
                position.SellPrice = lastKline.Close;
            }
            else
            {
                position.SellTime = endTime;
                position.SellPrice = position.BuyPrice;
            }
            position.Profit = position.Volume * (position.SellPrice!.Value - position.BuyPrice);
            position.ProfitRate = position.Profit / (position.Volume * position.BuyPrice) * 100;
            trades.Add(position);
        }
        positions.Clear();

        result.FinalCapital = capital;
        result.TotalReturn = (capital - config.InitialCapital) / config.InitialCapital * 100;
        result.AnnualizedReturn = CalculateAnnualizedReturn(result.TotalReturn, startTime, endTime);
        result.Trades = trades;
        result.TradeCount = trades.Count;
        result.DailyNavs = allDailyNavs;

        if (trades.Count > 0)
        {
            result.WinRate = (decimal)trades.Count(t => t.Profit > 0) / trades.Count * 100;
            var avgProfit = trades.Where(t => t.Profit > 0).Select(t => t.Profit).DefaultIfEmpty(0).Average();
            var avgLoss = Math.Abs(trades.Where(t => t.Profit < 0).Select(t => t.Profit).DefaultIfEmpty(0).Average());
            result.ProfitLossRatio = avgLoss > 0 ? avgProfit / avgLoss : 0;
        }

        // ★ FIX #5: 逐日 MTM 最大回撤
        result.MaxDrawdown = CalculateMaxDrawdownFromNav(allDailyNavs);

        // ★ FIX #4: 逐日净值 Sharpe
        result.SharpeRatio = CalculateSharpeRatioFromNav(allDailyNavs);

        return result;
    }

    public async Task<BacktestResult> RunSingleStockBacktestAsync(
        BacktestConfig config, IStrategy strategy, string code,
        DateTime startTime, DateTime endTime)
    {
        return await RunBacktestAsync(config, strategy, new List<string> { code }, startTime, endTime);
    }

    // ========== 数据加载 ==========

    private async Task<List<KlineData>> GetKlinesAsync(string code, DateTime startTime, DateTime endTime)
    {
        try
        {
            var provider = _dataProviderResolver.GetDefaultProvider();
            if (provider == null)
            {
                _logger.LogWarning("No data provider available for backtest");
                return new List<KlineData>();
            }

            const int maxKlines = 2000;
            var calendarDays = (DateTime.Now.Date - startTime.Date).TotalDays;
            var estimatedTradingDays = (int)Math.Ceiling(calendarDays * 0.72) + 80;
            var count = Math.Clamp(estimatedTradingDays, 120, maxKlines);

            var klines = await provider.GetKlinesAsync(code, Core.Enums.KlineInterval.Daily, count);
            if (klines == null || klines.Count == 0)
            {
                _logger.LogWarning("Provider returned no klines for {Code}", code);
                return new List<KlineData>();
            }

            var inRange = klines
                .Where(k => k.DateTime >= startTime && k.DateTime <= endTime)
                .OrderBy(k => k.DateTime)
                .DistinctBy(k => k.DateTime)
                .ToList();

            var oldest = klines.Min(k => k.DateTime);
            if (oldest > startTime.Date.AddDays(1))
            {
                _logger.LogWarning(
                    "回测数据未完整覆盖区间 {Code}: 请求起始 {Start:yyyy-MM-dd}，实际最早 {Oldest:yyyy-MM-dd}（受数据源 {Count} 条上限限制）",
                    code, startTime, oldest, count);
            }

            return inRange;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get klines for {Code}", code);
            return new List<KlineData>();
        }
    }

    // ========== 计算工具 ==========

    private decimal CalculateAnnualizedReturn(decimal totalReturn, DateTime startTime, DateTime endTime)
    {
        var years = (endTime - startTime).TotalDays / 365;
        if (years <= 0) return 0;
        return (decimal)(Math.Pow((double)(1 + totalReturn / 100), 1 / years) - 1) * 100;
    }

    /// <summary>★ FIX #5: 基于逐日净值的最大回撤计算。</summary>
    private static decimal CalculateMaxDrawdownFromNav(List<DailyNav> navs)
    {
        if (navs.Count < 2) return 0;

        var peak = navs[0].Nav;
        var maxDrawdown = 0m;
        foreach (var nav in navs)
        {
            if (nav.Nav > peak) peak = nav.Nav;
            var dd = (peak - nav.Nav) / peak * 100;
            if (dd > maxDrawdown) maxDrawdown = dd;
        }
        return maxDrawdown;
    }

    /// <summary>★ FIX #4: 基于逐日净值日收益率的 Sharpe Ratio。</summary>
    private static decimal CalculateSharpeRatioFromNav(List<DailyNav> navs)
    {
        if (navs.Count < 3) return 0;

        var dailyReturns = new List<decimal>();
        for (int i = 1; i < navs.Count; i++)
        {
            if (navs[i - 1].Nav > 0)
                dailyReturns.Add((navs[i].Nav - navs[i - 1].Nav) / navs[i - 1].Nav * 100);
        }

        if (dailyReturns.Count < 2) return 0;

        var avgReturn = dailyReturns.Average();
        var variance = dailyReturns.Sum(r => (r - avgReturn) * (r - avgReturn)) / (dailyReturns.Count - 1);
        var stdDev = (decimal)Math.Sqrt((double)Math.Max(0, (double)variance));

        // 年化：日收益率 × √252
        return stdDev > 0 ? avgReturn / stdDev * (decimal)Math.Sqrt(252) : 0;
    }
}
