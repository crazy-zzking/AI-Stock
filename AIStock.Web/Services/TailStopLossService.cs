using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIStock.Web.Services;

/// <summary>
/// 盘中止损监控服务：交易时段内每 60 秒拉腾讯实时行情，
/// 一旦持仓价格跌破止损线立即平仓，无需等到 14:55。
/// 与 TailMarketBuyHostedService（尾盘买入+到期卖出）完全解耦。
/// </summary>
public class TailStopLossService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProviderResolver _providerResolver;
    private readonly ITradingCalendar _calendar;
    private readonly TailBuyOptions _options;
    private readonly ILogger<TailStopLossService> _logger;

    // 盘中监控时段：9:25 ~ 15:05
    private static readonly TimeSpan TradingStart = new(9, 25, 0);
    private static readonly TimeSpan TradingEnd   = new(15, 5, 0);

    public TailStopLossService(
        IServiceScopeFactory scopeFactory,
        IDataProviderResolver providerResolver,
        ITradingCalendar calendar,
        IOptions<TailBuyOptions> options,
        ILogger<TailStopLossService> logger)
    {
        _scopeFactory     = scopeFactory;
        _providerResolver = providerResolver;
        _calendar         = calendar;
        _options          = options.Value;
        _logger           = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("盘中止损监控服务启动 (StopLossPercent={Pct}%)", _options.StopLossPercent);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now   = DateTime.Now;
                var today = DateOnly.FromDateTime(now);

                if (now.TimeOfDay >= TradingStart && now.TimeOfDay <= TradingEnd
                    && await _calendar.IsTradingDayAsync(today.ToDateTime(TimeOnly.MinValue), stoppingToken))
                {
                    await CheckStopLossAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "盘中止损监控循环异常");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>扫描开仓持仓，实时报价触及止损线则立即平仓。</summary>
    public async Task<int> CheckStopLossAsync(CancellationToken ct = default)
    {
        using var scope        = _scopeFactory.CreateScope();
        var db                 = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var orderManager       = scope.ServiceProvider.GetRequiredService<IOrderManager>();
        var tradingGate        = scope.ServiceProvider.GetRequiredService<ITradingGate>();

        var opens = await db.TailPosition.Where(t => t.Status == 0).ToListAsync(ct);
        if (opens.Count == 0) return 0;

        // 拉腾讯实时行情（批量）
        var codes    = opens.Select(t => t.Code).Distinct().ToList();
        var provider = _providerResolver.GetPrimaryProvider(DataCapability.Quote);
        if (provider == null)
        {
            _logger.LogWarning("止损监控：无可用实时行情数据源，跳过本轮");
            return 0;
        }

        var quotes      = await provider.GetQuotesAsync(codes);
        var priceByCode = quotes.ToDictionary(q => q.Code, q => q.Price);

        var dryRun    = tradingGate.Mode == TradingMode.DryRun;
        var triggered = 0;

        foreach (var pos in opens)
        {
            if (ct.IsCancellationRequested) break;
            if (!priceByCode.TryGetValue(pos.Code, out var price) || price <= 0) continue;
            if (price > pos.StopLossPrice) continue; // 未触及止损，跳过

            bool ok;
            if (dryRun)
            {
                ok = true;
                var pnl = pos.BuyPrice > 0
                    ? Math.Round((price - pos.BuyPrice) / pos.BuyPrice * 100m, 2) : 0m;
                _logger.LogWarning(
                    "[DryRun] 盘中止损 {Code} 现价={Price} 止损线={SL} 量={Vol} 成本={Cost} 盈亏={Pnl}%",
                    pos.Code, price, pos.StopLossPrice, pos.Volume, pos.BuyPrice, pnl);
            }
            else
            {
                var req = new OrderRequest
                {
                    Code         = pos.Code,
                    Side         = "sell",
                    OrderType    = OrderType.Limit,
                    Price        = price,
                    Volume       = pos.Volume,
                    StrategyName = $"{pos.Strategy}-stop-loss",
                    SignalId     = pos.SignalId,
                };
                var res = await orderManager.PlaceOrderAsync(req);
                ok = res.Success;
                _logger.LogWarning(
                    "盘中止损 {Code} 现价={Price} 止损线={SL} → success={OK} msg={Msg}",
                    pos.Code, price, pos.StopLossPrice, res.Success, res.Message);
            }

            if (ok)
            {
                pos.Status    = 1;
                pos.SellDate  = DateTime.Now;
                pos.SellPrice = price;
                pos.SellReason = "stop-loss";
                triggered++;
            }
        }

        if (triggered > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("盘中止损完成：本轮触发 {Count} 笔", triggered);
        }

        return triggered;
    }
}
