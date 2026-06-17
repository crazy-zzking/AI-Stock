using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 回测编排：把已落库的历史选股结果（selection_result）当作信号，用 kline_data 跑统一回测引擎，
/// 得到该套选股在历史上的真实表现（胜率 / 平均收益 / 盈亏比 / 回撤）。
/// </summary>
public class BacktestService
{
    private readonly AIStockDbContext _db;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(AIStockDbContext db, ILogger<BacktestService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 回测历史选股结果。把 [from,to] 区间内每条选股记录的标的作为信号（同日同股去重），
    /// 按 config 的持有期与买点跑回测。
    /// </summary>
    public async Task<BacktestReport> BacktestHistoryAsync(
        BacktestConfig config, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = _db.SelectionResult.AsQueryable();
        if (from != null) q = q.Where(r => r.TradingDate >= from.Value.Date);
        if (to != null) q = q.Where(r => r.TradingDate <= to.Value.Date);
        var rows = await q.OrderBy(r => r.TradingDate).ToListAsync(ct);

        var signals = new List<BacktestSignal>();
        var seen = new HashSet<(DateTime, string)>();
        foreach (var row in rows)
        {
            foreach (var p in Deserialize(row.ResultsJson))
            {
                if (string.IsNullOrEmpty(p.Code)) continue;
                if (seen.Add((row.TradingDate.Date, p.Code)))
                    signals.Add(new BacktestSignal { Date = row.TradingDate, Code = p.Code, Name = p.Name });
            }
        }

        if (signals.Count == 0)
        {
            _logger.LogInformation("回测：区间内无历史选股信号");
            return new BacktestReport { HoldDays = config.HoldDays, Entry = config.Entry.ToString() };
        }

        var bars = await LoadBarsAsync(signals.Select(s => s.Code).Distinct().ToList(),
            signals.Min(s => s.Date).Date, ct);

        // 尝试加载基准 (沪深300)
        var benchmarkBars = await LoadBenchmarkBarsAsync(signals.Min(s => s.Date).Date,
            signals.Max(s => s.Date).Date, ct);

        var engine = new BacktestEngine();
        var report = engine.Run(signals, bars, config, benchmarkBars);
        _logger.LogInformation("回测完成：信号 {Sig} 笔，成交 {Exe}，胜率 {Win}%，平均收益 {Avg}%，盈亏比 {Pf}",
            report.TotalSignals, report.ExecutedTrades, report.WinRatePct, report.AvgReturnPct, report.ProfitFactor);

        // ★ 持久化回测结果
        try
        {
            var configJson = System.Text.Json.JsonSerializer.Serialize(config);
            var reportJson = report.ToSummaryJson(); // 精简：不落 Trades/EquityCurve
            _db.BacktestResult.Add(new AIStock.Infrastructure.Database.Entities.BacktestResultEntity
            {
                RunAt = DateTime.UtcNow,
                BacktestType = "selection",
                ConfigJson = configJson,
                ReportJson = reportJson,
                ExecutedTrades = report.ExecutedTrades,
                WinRatePct = (decimal)report.WinRatePct,
                AvgReturnPct = (decimal)report.AvgReturnPct,
                SharpeRatio = report.SharpeRatio,
                MaxDrawdownPct = (decimal)report.MaxDrawdownPct,
                AlphaPct = report.Alpha,
                Beta = report.Beta,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("回测结果持久化失败（非致命）：{Msg}", ex.Message);
        }

        return report;
    }

    private async Task<Dictionary<string, List<BacktestBar>>> LoadBarsAsync(
        List<string> codes, DateTime minDate, CancellationToken ct)
    {
        const string interval = nameof(KlineInterval.Daily);
        var raw = await _db.KlineData
            .Where(k => k.Interval == interval && codes.Contains(k.Code) && k.DateTime >= minDate)
            .Select(k => new { k.Code, k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume })
            .ToListAsync(ct);

        return raw
            .GroupBy(k => k.Code)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(k => k.DateTime)
                      .Select(k => new BacktestBar
                      {
                          Date = k.DateTime,
                          Open = k.Open,
                          High = k.High,
                          Low = k.Low,
                          Close = k.Close,
                          Volume = k.Volume,
                      })
                      .ToList());
    }

    /// <summary>加载沪深300基准日K（secid: 000300）。</summary>
    private async Task<List<BacktestBar>?> LoadBenchmarkBarsAsync(
        DateTime minDate, DateTime maxDate, CancellationToken ct)
    {
        try
        {
            const string interval = nameof(KlineInterval.Daily);
            const string hs300 = "000300";
            var rows = await _db.KlineData
                .Where(k => k.Interval == interval && k.Code == hs300
                    && k.DateTime >= minDate.AddDays(-5) && k.DateTime <= maxDate.AddDays(1))
                .OrderBy(k => k.DateTime)
                .Select(k => new { k.DateTime, k.Open, k.High, k.Low, k.Close, k.Volume })
                .ToListAsync(ct);
            if (rows.Count == 0) return null;
            return rows.Select(k => new BacktestBar
            {
                Date = k.DateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume,
            }).ToList();
        }
        catch
        {
            return null; // 基准不可用时优雅降级
        }
    }

    private static List<StockSelectionResult> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<StockSelectionResult>>(json) ?? new(); }
        catch { return new(); }
    }
}
