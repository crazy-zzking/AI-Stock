using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Services;

/// <summary>
/// 尾盘卖出服务：扫描尾盘未平仓持仓(tail_position)，按"持有到期 / 止损"平仓。
/// DryRun 下直接模拟平仓（券商账户无该模拟持仓，OrderManager 卖出会因可用持仓不足被拒）；
/// Live 经 OrderManager 真卖。现价取当日 daily_market_snapshot 收盘/实时价。
/// </summary>
public class TailSellService
{
    private readonly AIStockDbContext _db;
    private readonly IOrderManager _orderManager;
    private readonly ITradingGate _tradingGate;
    private readonly ILogger<TailSellService> _logger;

    public TailSellService(
        AIStockDbContext db, IOrderManager orderManager, ITradingGate tradingGate,
        ILogger<TailSellService> logger)
    {
        _db = db;
        _orderManager = orderManager;
        _tradingGate = tradingGate;
        _logger = logger;
    }

    /// <summary>执行一次尾盘卖出扫描，返回平仓笔数。</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var opens = await _db.TailPosition.Where(t => t.Status == 0).ToListAsync(ct);
        if (opens.Count == 0)
        {
            _logger.LogInformation("尾盘卖出：无未平仓持仓");
            return 0;
        }

        var codes = opens.Select(t => t.Code).Distinct().ToList();
        var latestDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        var priceByCode = latestDate == null
            ? new Dictionary<string, decimal>()
            : await _db.DailyMarketSnapshot
                .Where(s => s.Date == latestDate && codes.Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, s => s.Close, ct);

        var dryRun = _tradingGate.Mode == TradingMode.DryRun;
        var sold = 0;

        foreach (var pos in opens)
        {
            if (ct.IsCancellationRequested) break;

            if (!priceByCode.TryGetValue(pos.Code, out var price) || price <= 0)
            {
                _logger.LogWarning("尾盘卖出跳过 {Code}：无当日现价", pos.Code);
                continue;
            }

            // 已持有交易日数 = 买入日之后的快照交易日数
            var heldDays = await _db.DailyMarketSnapshot
                .Where(s => s.Code == pos.Code && s.Date.Date > pos.BuyDate.Date)
                .Select(s => s.Date.Date).Distinct().CountAsync(ct);

            string? reason = null;
            if (price <= pos.StopLossPrice) reason = "stop-loss";
            else if (heldDays >= pos.HoldDays) reason = "hold-expired";
            if (reason == null) continue;

            bool ok;
            if (dryRun)
            {
                ok = true; // 模拟成交
                var pnl = pos.BuyPrice > 0 ? Math.Round((price - pos.BuyPrice) / pos.BuyPrice * 100m, 2) : 0m;
                _logger.LogInformation("[DryRun] 尾盘卖出 {Code} reason={Reason} 量={Vol}@{Price} 成本={Cost} 持有{Days}日 盈亏{Pnl}%",
                    pos.Code, reason, pos.Volume, price, pos.BuyPrice, heldDays, pnl);
            }
            else
            {
                var req = new OrderRequest
                {
                    Code = pos.Code, Side = "sell", OrderType = OrderType.Limit,
                    Price = price, Volume = pos.Volume,
                    StrategyName = $"{pos.Strategy}-tail-exit", SignalId = pos.SignalId,
                };
                var res = await _orderManager.PlaceOrderAsync(req);
                ok = res.Success;
                _logger.LogInformation("尾盘卖出 {Code} reason={Reason} 量={Vol}@{Price} → success={OK} msg={Msg}",
                    pos.Code, reason, pos.Volume, price, res.Success, res.Message);
            }

            if (ok)
            {
                pos.Status = 1;
                pos.SellDate = DateTime.Today;
                pos.SellPrice = price;
                pos.SellReason = reason;
                sold++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("尾盘卖出完成：平仓 {Sold}/{Total}", sold, opens.Count);
        return sold;
    }
}
