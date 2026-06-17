using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIStock.Web.Services;

/// <summary>
/// 尾盘选股入池服务：用选股引擎选 TOP-N → 规则计算买入/止损/止盈价位 → upsert 进 trade_candidate 候选池。
/// 不再自动下单；实际交易只能通过候选池手动确认（TradeCandidateController.Order）。
/// </summary>
public class TailMarketBuyService
{
    private readonly StockSelectionService _selection;
    private readonly ITradingGate _tradingGate;
    private readonly AIStockDbContext _db;
    private readonly TailBuyOptions _options;
    private readonly ILogger<TailMarketBuyService> _logger;

    public TailMarketBuyService(
        StockSelectionService selection, ITradingGate tradingGate,
        AIStockDbContext db, IOptions<TailBuyOptions> options, ILogger<TailMarketBuyService> logger)
    {
        _selection = selection;
        _tradingGate = tradingGate;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>执行一次尾盘选股 + 入候选池，返回入池条数。</summary>
    public async Task<int> RunAsync(CancellationToken ct = default, bool force = false)
    {
        if (!_options.Enabled && !force)
        {
            _logger.LogInformation("尾盘选股入池未启用 (TailBuy:Enabled=false)，跳过");
            return 0;
        }

        // 防呆：DryRunOnly 但闸门是实盘 → 拒绝（入池也不做，避免混淆）
        if (_options.DryRunOnly && _tradingGate.Mode == TradingMode.Live)
        {
            _logger.LogWarning("TailBuy:DryRunOnly=true 但 TradingGate=Live，跳过入池以防误实盘");
            return 0;
        }

        var picks = await _selection.SelectAsync(null, _options.Strategy, ct);
        var top = picks.Take(Math.Max(1, _options.TopN)).ToList();
        _logger.LogInformation("尾盘选股入池：策略[{Strategy}] 选出 {Count} 只，闸门模式={Mode}",
            _options.Strategy, top.Count, _tradingGate.Mode);

        if (top.Count == 0) return 0;

        var today = DateTime.Today;
        var codes = top.Select(p => p.Code).ToList();

        // 加载近 30 日 K 线用于价位计算
        var barsByCode = await LoadBarsAsync(codes, today, ct);

        // 加载当日已有的候选记录
        var existing = await _db.TradeCandidate
            .Where(c => c.TradingDate == today && codes.Contains(c.Code))
            .ToDictionaryAsync(c => c.Code, c => c, ct);

        int added = 0, updated = 0, skipped = 0;
        var addedEntities = new List<TradeCandidateEntity>(); // 暂存新增实体供后面统一 SaveChanges

        foreach (var p in top)
        {
            if (ct.IsCancellationRequested) break;

            if (p.Close <= 0)
            {
                _logger.LogWarning("尾盘入池跳过 {Code}：快照价 <= 0", p.Code);
                continue;
            }

            var price = p.Close;
            var volume = (long)(_options.PerStockValue / price / 100m) * 100;
            if (volume < 100)
            {
                _logger.LogWarning("尾盘入池跳过 {Code}：金额 {Value} / 价 {Price} 不足 1 手",
                    p.Code, _options.PerStockValue, price);
                continue;
            }

            // 规则计算买入计划价位
            barsByCode.TryGetValue(p.Code, out var bars);
            var plan = PriceLevelCalculator.Compute(
                bars ?? new List<PriceLevelCalculator.PriceBar>(), price);

            var tags = p.HotConcepts.Count > 0
                ? p.HotConcepts
                : p.Concepts.Take(3).ToList();
            var tagsJson = JsonSerializer.Serialize(tags, AIStock.Core.Json.AppJson.Default);

            if (existing.TryGetValue(p.Code, out var e))
            {
                if (e.Status != 0) { skipped++; continue; } // 用户已处理，不覆盖
                e.Name = p.Name;
                e.Score = p.TotalScore;
                e.RatingStars = p.RatingStars;
                e.Tags = tagsJson;
                e.TopStrategy = _options.Strategy;
                e.TopStrategyName = _options.Strategy;
                e.HitStrategies = JsonSerializer.Serialize(
                    new[] { _options.Strategy }, AIStock.Core.Json.AppJson.Default);
                e.HitCount = 1;
                e.Narrative = p.CoreLogic;
                e.RefClose = price;
                e.BuyLow = plan.BuyLow;
                e.BuyHigh = plan.BuyHigh;
                e.StopLoss = plan.StopLoss;
                e.TakeProfit = plan.TakeProfit;
                e.PlanBasis = plan.Basis;
                e.UpdatedAt = DateTime.Now;
                updated++;
            }
            else
            {
                var entity = new TradeCandidateEntity
                {
                    TradingDate = today,
                    Code = p.Code,
                    Name = p.Name,
                    Score = p.TotalScore,
                    RatingStars = p.RatingStars,
                    Tags = tagsJson,
                    TopStrategy = _options.Strategy,
                    TopStrategyName = _options.Strategy,
                    HitStrategies = JsonSerializer.Serialize(
                        new[] { _options.Strategy }, AIStock.Core.Json.AppJson.Default),
                    HitCount = 1,
                    Narrative = p.CoreLogic,
                    RefClose = price,
                    BuyLow = plan.BuyLow,
                    BuyHigh = plan.BuyHigh,
                    StopLoss = plan.StopLoss,
                    TakeProfit = plan.TakeProfit,
                    PlanBasis = plan.Basis,
                    Status = 0,
                };
                _db.TradeCandidate.Add(entity);
                addedEntities.Add(entity);
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "尾盘选股入池完成：{Date} 新增 {Added} 刷新 {Updated} 跳过(已处理) {Skipped}",
            today.ToString("yyyy-MM-dd"), added, updated, skipped);

        return added + updated;
    }

    /// <summary>取各股近 30 自然日的日 K，供价位计算。</summary>
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
