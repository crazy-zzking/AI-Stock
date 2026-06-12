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

    /// <summary>跑全部策略并落库。返回汇总与各策略入选明细（供推送报告）。</summary>
    public async Task<DailySelectionRun> RunAllAsync(CancellationToken ct = default)
    {
        var strategies = await _selection.ListStrategiesAsync(ct);
        var run = new DailySelectionRun();

        foreach (var s in strategies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var picks = await _selection.RunAndSaveAsync(criteria: null, strategyKey: s.Key, ct);
                run.Ok++;
                run.Signals += picks.Count;
                run.PicksByStrategy.Add((s.Name, picks));
                _logger.LogInformation("每日选股：策略[{Key}] 入选 {Count} 只", s.Key, picks.Count);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                run.Failed++;
                _logger.LogWarning(ex, "每日选股：策略[{Key}] 失败，跳过", s.Key);
            }
        }

        _logger.LogInformation("每日选股完成：成功 {Ok} 策略 / 失败 {Failed}，共 {Signals} 个信号",
            run.Ok, run.Failed, run.Signals);
        return run;
    }
}

/// <summary>一次全策略选股的结果汇总。</summary>
public class DailySelectionRun
{
    public int Ok { get; set; }
    public int Failed { get; set; }
    public int Signals { get; set; }

    /// <summary>各策略入选明细（策略显示名, 入选列表）。</summary>
    public List<(string StrategyName, List<Core.Models.StockSelectionResult> Picks)> PicksByStrategy { get; } = new();

    /// <summary>
    /// 推送用文本报告：每策略列前 3 只（代码 名称，带小作文标记），空策略不列。
    /// </summary>
    public string FormatReport(string title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(title);
        foreach (var (name, picks) in PicksByStrategy)
        {
            if (picks.Count == 0) continue;
            var items = picks.Take(3).Select(p =>
                $"{p.Name}({p.Code}){(p.KnowledgeStarNotes.Count > 0 ? "✉" : "")}");
            sb.AppendLine($"· {name}: {string.Join("、", items)}{(picks.Count > 3 ? $" 等{picks.Count}只" : "")}");
        }
        if (Failed > 0) sb.AppendLine($"⚠ {Failed} 个策略执行失败（详见任务日志）");
        sb.Append($"共 {Signals} 个信号 | ✉=有小作文 | 详情看选股页/记分板");
        return sb.ToString();
    }
}
