using AIStock.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace AIStock.Web.Services;

/// <summary>
/// 尾盘自动下单后台服务：每 30 秒轮询，交易日到达 DecisionTime(默认14:55)窗口当天首次触发：
/// 1. TailSellService.RunAsync() — 到期(hold-expired)平仓
/// 2. TailMarketBuyService.RunAsync() — 新选股买入
/// 注：止损由独立的 TailStopLossService 盘中实时监控，不在此处处理。
/// </summary>
public class TailMarketBuyHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITradingCalendar _calendar;
    private readonly TailBuyOptions _options;
    private readonly ILogger<TailMarketBuyHostedService> _logger;

    public TailMarketBuyHostedService(
        IServiceScopeFactory scopeFactory, ITradingCalendar calendar,
        IOptions<TailBuyOptions> options, ILogger<TailMarketBuyHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _calendar = calendar;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("尾盘自动下单后台服务启动 (Enabled={Enabled}, DecisionTime={Time}, DryRunOnly={Dry})",
            _options.Enabled, _options.DecisionTime, _options.DryRunOnly);

        DateOnly? lastRun = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled && TimeSpan.TryParse(_options.DecisionTime, out var decisionT))
                {
                    var now = DateTime.Now;
                    var today = DateOnly.FromDateTime(now);
                    var windowEnd = decisionT.Add(TimeSpan.FromMinutes(Math.Max(1, _options.WindowMinutes)));

                    if (lastRun != today && now.TimeOfDay >= decisionT && now.TimeOfDay < windowEnd)
                    {
                        lastRun = today; // 当天只触发一次（无论是否交易日都标记，避免反复判断）
                        if (await _calendar.IsTradingDayAsync(today.ToDateTime(TimeOnly.MinValue), stoppingToken))
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var sell = scope.ServiceProvider.GetRequiredService<TailSellService>();
                            var buy = scope.ServiceProvider.GetRequiredService<TailMarketBuyService>();
                            await sell.RunAsync(stoppingToken);  // 先处理到期/止损卖出
                            await buy.RunAsync(stoppingToken);   // 再买入当日新选股
                        }
                        else
                        {
                            _logger.LogDebug("非交易日，尾盘下单跳过");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "尾盘下单后台循环异常");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
