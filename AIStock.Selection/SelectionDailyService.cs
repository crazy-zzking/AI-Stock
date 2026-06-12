using AIStock.Selection.Strategies;
using Microsoft.Extensions.Logging;

namespace AIStock.Selection;

/// <summary>
/// 每日全策略选股留痕：把所有策略（内置 + DB 自建）各按生效配置跑一遍并追加 selection_result。
/// 只留痕不下单——保证策略记分板每天有完整信号数据，不依赖有人打开页面或手动触发。
/// 单策略失败隔离，不影响其他策略。
/// </summary>
public class SelectionDailyService
{
    private readonly StockSelectionService _selection;
    private readonly ILogger<SelectionDailyService> _logger;

    public SelectionDailyService(StockSelectionService selection, ILogger<SelectionDailyService> logger)
    {
        _selection = selection;
        _logger = logger;
    }

    /// <summary>跑全部策略并落库。返回（成功策略数, 失败策略数, 信号总数）。</summary>
    public async Task<(int Ok, int Failed, int Signals)> RunAllAsync(CancellationToken ct = default)
    {
        var strategies = await _selection.ListStrategiesAsync(ct);
        int ok = 0, failed = 0, signals = 0;

        foreach (var s in strategies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var picks = await _selection.RunAndSaveAsync(criteria: null, strategyKey: s.Key, ct);
                ok++;
                signals += picks.Count;
                _logger.LogInformation("每日选股：策略[{Key}] 入选 {Count} 只", s.Key, picks.Count);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "每日选股：策略[{Key}] 失败，跳过", s.Key);
            }
        }

        _logger.LogInformation("每日选股完成：成功 {Ok} 策略 / 失败 {Failed}，共 {Signals} 个信号", ok, failed, signals);
        return (ok, failed, signals);
    }
}
