using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection;

/// <summary>
/// 交易候选池入池服务（纯规则、不依赖 LLM）。把指定选股批次的票按代码去重（保留最高分、记录命中策略），
/// 用规则核心逻辑作推荐理由、PriceLevelCalculator 算买入价/止损/止盈，upsert 进 trade_candidate。
/// 已被用户处理（已下单/已忽略）的候选不覆盖。
/// </summary>
public class TradeCandidateService
{
    private readonly AIStockDbContext _db;
    private readonly ILogger<TradeCandidateService> _logger;

    public TradeCandidateService(AIStockDbContext db, ILogger<TradeCandidateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>把给定选股批次（同一交易日）去重后入候选池。返回新增+刷新条数。</summary>
    public async Task<int> PopulateFromBatchesAsync(IReadOnlyList<long> batchIds, CancellationToken ct = default)
    {
        if (batchIds.Count == 0) return 0;

        var rows = await _db.SelectionResult
            .Where(r => batchIds.Contains(r.Id))
            .ToListAsync(ct);
        if (rows.Count == 0) return 0;

        var tradingDate = rows.Max(r => r.TradingDate).Date;

        // 展开所有批次的票，带上来源策略
        var picks = new List<(StockSelectionResult S, string StrategyName, long BatchId)>();
        foreach (var row in rows)
        {
            List<StockSelectionResult>? list;
            try { list = JsonSerializer.Deserialize<List<StockSelectionResult>>(row.ResultsJson); }
            catch { list = null; }
            if (list == null) continue;
            foreach (var s in list)
                if (!string.IsNullOrEmpty(s.Code))
                    picks.Add((s, row.StrategyName, row.Id));
        }
        if (picks.Count == 0) return 0;

        var groups = picks.GroupBy(p => p.S.Code).ToList();
        var codes = groups.Select(g => g.Key).ToList();
        var barsByCode = await LoadBarsAsync(codes, tradingDate, ct);
        var existing = await _db.TradeCandidate
            .Where(c => c.TradingDate == tradingDate && codes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, c => c, ct);

        int added = 0, updated = 0, skipped = 0;
        foreach (var g in groups)
        {
            // 同股取最高分的那条作代表
            var best = g.OrderByDescending(p => p.S.TotalScore).First();
            var s = best.S;
            var hitNames = g.Select(p => p.StrategyName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            var tags = s.HotConcepts.Count > 0 ? s.HotConcepts : s.Concepts.Take(3).ToList();

            barsByCode.TryGetValue(s.Code, out var bars);
            var plan = PriceLevelCalculator.Compute(bars ?? new List<PriceLevelCalculator.PriceBar>(), s.Close);

            var tagsJson = JsonSerializer.Serialize(tags, AIStock.Core.Json.AppJson.Default);
            var hitJson = JsonSerializer.Serialize(hitNames, AIStock.Core.Json.AppJson.Default);

            if (existing.TryGetValue(s.Code, out var e))
            {
                if (e.Status != 0) { skipped++; continue; } // 用户已处理，不覆盖
                e.Name = s.Name; e.Score = s.TotalScore; e.RatingStars = s.RatingStars; e.Tags = tagsJson;
                e.TopStrategy = best.StrategyName; e.TopStrategyName = best.StrategyName;
                e.HitStrategies = hitJson; e.HitCount = hitNames.Count; e.SourceBatchId = best.BatchId;
                e.Narrative = s.CoreLogic; e.RefClose = s.Close;
                e.BuyLow = plan.BuyLow; e.BuyHigh = plan.BuyHigh; e.StopLoss = plan.StopLoss;
                e.TakeProfit = plan.TakeProfit; e.PlanBasis = plan.Basis;
                updated++;
            }
            else
            {
                _db.TradeCandidate.Add(new TradeCandidateEntity
                {
                    TradingDate = tradingDate, Code = s.Code, Name = s.Name,
                    Score = s.TotalScore, RatingStars = s.RatingStars, Tags = tagsJson,
                    TopStrategy = best.StrategyName, TopStrategyName = best.StrategyName,
                    HitStrategies = hitJson, HitCount = hitNames.Count, SourceBatchId = best.BatchId,
                    Narrative = s.CoreLogic, RefClose = s.Close,
                    BuyLow = plan.BuyLow, BuyHigh = plan.BuyHigh, StopLoss = plan.StopLoss,
                    TakeProfit = plan.TakeProfit, PlanBasis = plan.Basis, Status = 0,
                });
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("交易候选池入池：{Date} 去重 {Codes} 只 → 新增 {Added} 刷新 {Updated} 跳过(已处理) {Skipped}",
            tradingDate.ToString("yyyy-MM-dd"), codes.Count, added, updated, skipped);
        return added + updated;
    }

    /// <summary>取各股选股日(含)前约 30 自然日的日 K，供价位计算。</summary>
    private async Task<Dictionary<string, List<PriceLevelCalculator.PriceBar>>> LoadBarsAsync(
        List<string> codes, DateTime tradingDate, CancellationToken ct)
    {
        const string interval = nameof(KlineInterval.Daily);
        var since = tradingDate.AddDays(-30);
        var rows = await _db.KlineData
            .Where(k => k.Interval == interval && codes.Contains(k.Code)
                        && k.DateTime >= since && k.DateTime <= tradingDate)
            .Select(k => new { k.Code, k.DateTime, k.High, k.Low, k.Close })
            .ToListAsync(ct);

        return rows
            .GroupBy(k => k.Code)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.DateTime)
                      .Select(x => new PriceLevelCalculator.PriceBar(x.High, x.Low, x.Close))
                      .ToList());
    }
}
