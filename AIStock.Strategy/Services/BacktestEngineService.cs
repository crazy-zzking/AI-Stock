using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Strategy.Services;

/// <summary>
/// 回测引擎实现
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

    public async Task<BacktestResult> RunBacktestAsync(BacktestConfig config, IStrategy strategy, List<string> codes, DateTime startTime, DateTime endTime)
    {
        var result = new BacktestResult
        {
            StrategyName = strategy.Name,
            StartTime = startTime,
            EndTime = endTime,
            InitialCapital = config.InitialCapital
        };

        var capital = config.InitialCapital;
        var positions = new Dictionary<string, BacktestTrade>();
        var trades = new List<BacktestTrade>();
        var dailyNavs = new List<DailyNav>();

        foreach (var code in codes)
        {
            try
            {
                var klines = await GetKlinesAsync(code, startTime, endTime);
                if (klines == null || klines.Count < 60)
                    continue;

                for (int i = 60; i < klines.Count; i++)
                {
                    var currentKline = klines[i];
                    var historyKlines = klines.Take(i + 1).ToList();
                    var indicators = _featureCalculator.CalculateAll(code, historyKlines);

                    var signal = await strategy.GenerateSignalAsync(code, historyKlines, indicators);
                    if (signal == null) continue;

                    if (signal.SignalType == SignalType.Buy && !positions.ContainsKey(code))
                    {
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
                    else if (signal.SignalType == SignalType.Sell && positions.ContainsKey(code))
                    {
                        var position = positions[code];
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
                }

                foreach (var position in positions.Values)
                {
                    var lastKline = klines.Last();
                    position.SellTime = lastKline.DateTime;
                    position.SellPrice = lastKline.Close;
                    position.Profit = position.Volume * (lastKline.Close - position.BuyPrice);
                    position.ProfitRate = position.Profit / (position.Volume * position.BuyPrice) * 100;
                    trades.Add(position);
                }
                positions.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error backtesting {Code}", code);
            }
        }

        result.FinalCapital = capital;
        result.TotalReturn = (capital - config.InitialCapital) / config.InitialCapital * 100;
        result.AnnualizedReturn = CalculateAnnualizedReturn(result.TotalReturn, startTime, endTime);
        result.Trades = trades;
        result.TradeCount = trades.Count;

        if (trades.Count > 0)
        {
            result.WinRate = (decimal)trades.Count(t => t.Profit > 0) / trades.Count * 100;
            var avgProfit = trades.Where(t => t.Profit > 0).Select(t => t.Profit).DefaultIfEmpty(0).Average();
            var avgLoss = Math.Abs(trades.Where(t => t.Profit < 0).Select(t => t.Profit).DefaultIfEmpty(0).Average());
            result.ProfitLossRatio = avgLoss > 0 ? avgProfit / avgLoss : 0;
        }

        result.MaxDrawdown = CalculateMaxDrawdown(trades, config.InitialCapital);
        result.SharpeRatio = CalculateSharpeRatio(trades, config.InitialCapital);

        return result;
    }

    public async Task<BacktestResult> RunSingleStockBacktestAsync(BacktestConfig config, IStrategy strategy, string code, DateTime startTime, DateTime endTime)
    {
        return await RunBacktestAsync(config, strategy, new List<string> { code }, startTime, endTime);
    }

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

            // Provider 的 GetKlinesAsync 取的是「最近 N 条日线」（TdxProvider 内部已按 800 分页）。
            // 因此请求条数需覆盖 startTime 至今的跨度，再按区间过滤。
            // 估算：日历天数 → 交易日约 0.7 折算，外加缓冲。
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

            // 数据未能回溯到 startTime（受 provider 上限限制），提示区间未完整覆盖
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

    private decimal CalculateAnnualizedReturn(decimal totalReturn, DateTime startTime, DateTime endTime)
    {
        var years = (endTime - startTime).TotalDays / 365;
        if (years <= 0) return 0;
        return (decimal)(Math.Pow((double)(1 + totalReturn / 100), 1 / years) - 1) * 100;
    }

    private decimal CalculateMaxDrawdown(List<BacktestTrade> trades, decimal initialCapital)
    {
        if (trades.Count == 0) return 0;

        var capital = initialCapital;
        var peak = capital;
        var maxDrawdown = 0m;

        foreach (var trade in trades.OrderBy(t => t.SellTime))
        {
            capital += trade.Profit;
            if (capital > peak)
            {
                peak = capital;
            }
            var drawdown = (peak - capital) / peak * 100;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return maxDrawdown;
    }

    private decimal CalculateSharpeRatio(List<BacktestTrade> trades, decimal initialCapital)
    {
        if (trades.Count < 2) return 0;

        var returns = trades.Select(t => t.ProfitRate).ToList();
        var avgReturn = returns.Average();
        var stdDev = (decimal)Math.Sqrt((double)returns.Select(r => (r - avgReturn) * (r - avgReturn)).Average());

        return stdDev > 0 ? avgReturn / stdDev * (decimal)Math.Sqrt(252) : 0;
    }
}
