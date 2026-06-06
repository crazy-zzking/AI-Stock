using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Selection;
using Microsoft.Extensions.Options;

namespace AIStock.Web.Services;

/// <summary>
/// 尾盘自动下单衔接服务：用选股引擎选 TOP-N → 等额仓位构造买单 → 经 OrderManager（含全部安全护栏）下单。
/// 直接由选股结果驱动，不走 AlphaAgent/技术策略。默认 DryRun（OrderManager 内按 TradingGate.Mode 处理）。
/// </summary>
public class TailMarketBuyService
{
    private readonly StockSelectionService _selection;
    private readonly IOrderManager _orderManager;
    private readonly ITradingGate _tradingGate;
    private readonly TailBuyOptions _options;
    private readonly ILogger<TailMarketBuyService> _logger;

    public TailMarketBuyService(
        StockSelectionService selection, IOrderManager orderManager, ITradingGate tradingGate,
        IOptions<TailBuyOptions> options, ILogger<TailMarketBuyService> logger)
    {
        _selection = selection;
        _orderManager = orderManager;
        _tradingGate = tradingGate;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>执行一次尾盘选股 + 下单，返回各标的下单结果。</summary>
    public async Task<List<OrderResult>> RunAsync(CancellationToken ct = default, bool force = false)
    {
        var results = new List<OrderResult>();

        if (!_options.Enabled && !force)
        {
            _logger.LogInformation("尾盘自动下单未启用 (TailBuy:Enabled=false)，跳过");
            return results;
        }

        // 防呆：DryRunOnly 但闸门是实盘 → 拒绝，避免误下真单
        if (_options.DryRunOnly && _tradingGate.Mode == TradingMode.Live)
        {
            _logger.LogWarning("TailBuy:DryRunOnly=true 但 TradingGate=Live，跳过下单以防误实盘");
            return results;
        }

        var picks = await _selection.SelectAsync(null, _options.Strategy, ct);
        var top = picks.Take(Math.Max(1, _options.TopN)).ToList();
        _logger.LogInformation("尾盘下单：策略[{Strategy}] 选出 {Count} 只，闸门模式={Mode}",
            _options.Strategy, top.Count, _tradingGate.Mode);

        foreach (var p in top)
        {
            if (ct.IsCancellationRequested) break;

            var price = p.Close;
            if (price <= 0)
            {
                _logger.LogWarning("尾盘下单跳过 {Code}：快照价 <= 0", p.Code);
                continue;
            }

            var volume = (long)(_options.PerStockValue / price / 100m) * 100;
            if (volume < 100)
            {
                _logger.LogWarning("尾盘下单跳过 {Code}：金额 {Value} / 价 {Price} 不足 1 手",
                    p.Code, _options.PerStockValue, price);
                continue;
            }

            var req = new OrderRequest
            {
                Code = p.Code,
                Side = "buy",
                OrderType = OrderType.Limit,
                Price = price,
                Volume = volume,
                StrategyName = $"{_options.Strategy}-tail",
                SignalId = $"tail-{DateTime.Now:yyyyMMdd}-{p.Code}",
            };

            var res = await _orderManager.PlaceOrderAsync(req);
            results.Add(res);
            _logger.LogInformation("尾盘下单 {Code} {Name} 量={Vol}@{Price} → success={OK} status={Status} msg={Msg}",
                p.Code, p.Name, volume, price, res.Success, res.Status, res.Message);
        }

        var ok = results.Count(r => r.Success);
        _logger.LogInformation("尾盘下单完成：{Ok}/{Total} 成功", ok, results.Count);
        return results;
    }
}
