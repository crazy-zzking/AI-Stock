using System.Text.Json;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Backtest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Controllers;

/// <summary>
/// 选股 API — 短线弹性品种 TOP-N（两级漏斗：活跃度粗筛 → 多因子打分）。
/// 数据源为每日快照表，需先由 Worker 的 market-snapshot / dragon-tiger 任务采集。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SelectionController : ControllerBase
{
    private readonly StockSelectionService _selection;
    private readonly SelectionConfigService _config;
    private readonly BacktestService _backtest;
    private readonly ReplayBacktestService _replay;
    private readonly ILogger<SelectionController> _logger;

    public SelectionController(StockSelectionService selection, SelectionConfigService config,
        BacktestService backtest, ReplayBacktestService replay, ILogger<SelectionController> logger)
    {
        _selection = selection;
        _config = config;
        _backtest = backtest;
        _replay = replay;
        _logger = logger;
    }

    /// <summary>可用选股策略清单（key/name/说明/适用环境，含数据库自建策略）。</summary>
    [HttpGet("strategies")]
    public async Task<ActionResult> Strategies(CancellationToken ct)
    {
        var list = await _selection.ListStrategiesAsync(ct);
        return Ok(list.Select(s => new { s.Key, s.Name, s.Description, s.PreferredRegime, s.UsesPatterns }));
    }

    /// <summary>可选 K 线形态目录（key/name），供"K线形态"策略在选股时自选形态。</summary>
    [HttpGet("patterns")]
    public ActionResult Patterns()
        => Ok(CandlePatternAnalyzer.PatternKeys.Select(k => new { key = k, name = CandlePatternAnalyzer.DisplayName(k) }));

    /// <summary>按自定义条件实时选股（不落库，调试/试参用）。</summary>
    [HttpPost("screen")]
    public async Task<ActionResult<List<StockSelectionResult>>> Screen(
        [FromBody] SelectionCriteria? criteria, [FromQuery] string? strategy, CancellationToken ct)
    {
        var results = await _selection.SelectAsync(criteria, strategy, ct);
        return Ok(results);
    }

    /// <summary>返回当日已冻结的选股结果（供面板展示）。</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<List<StockSelectionResult>>> Latest([FromQuery] int topN = 5, CancellationToken ct = default)
    {
        var results = await _selection.GetOrCreateLatestAsync(topN, ct);
        return Ok(results);
    }

    /// <summary>重新选股并追加一条历史记录。</summary>
    [HttpPost("run")]
    public async Task<ActionResult<List<StockSelectionResult>>> Run(
        [FromBody] SelectionCriteria? criteria, [FromQuery] string? strategy, CancellationToken ct)
    {
        var results = await _selection.RunAndSaveAsync(criteria, strategy, ct);
        return Ok(results);
    }

    /// <summary>导入外部选股结果。</summary>
    [HttpPost("import")]
    public async Task<ActionResult> Import([FromBody] List<StockSelectionResult>? picks, [FromQuery] string? date, CancellationToken ct = default)
    {
        if (picks == null || picks.Count == 0) return BadRequest("无选股数据");
        var d = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.Today.AddDays(-1);
        var n = await _selection.ImportAsync(picks, d, ct);
        return Ok(new { imported = n, tradingDate = d.Date.ToString("yyyy-MM-dd") });
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<SelectionHistoryItem>>> History([FromQuery] int take = 30, CancellationToken ct = default)
    {
        var items = await _selection.GetHistoryAsync(take, ct);
        return Ok(items);
    }

    [HttpGet("history/page")]
    public async Task<ActionResult<SelectionHistoryPage>> HistoryPage([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _selection.GetHistoryPageAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("history/{id:long}")]
    public async Task<ActionResult<List<StockSelectionResult>>> HistoryDetail(long id, CancellationToken ct)
    {
        var results = await _selection.GetByIdAsync(id, ct);
        return Ok(results);
    }

    [HttpGet("history/{id:long}/performance")]
    public async Task<ActionResult<SelectionPerformance>> HistoryPerformance(long id, CancellationToken ct)
    {
        var perf = await _selection.GetPerformanceAsync(id, ct);
        return perf == null ? NotFound() : Ok(perf);
    }

    [HttpPost("history/{id:long}/review")]
    public async Task<ActionResult> ReviewBatch(long id, CancellationToken ct)
        => await _selection.RequestReviewAsync(id, ct) ? Ok(new { queued = true }) : NotFound();

    [HttpPost("history/{id:long}/enpool")]
    public async Task<ActionResult> Enpool(long id, CancellationToken ct = default)
    {
        var db = HttpContext.RequestServices.GetRequiredService<AIStock.Infrastructure.Database.Context.AIStockDbContext>();
        var entity = await db.SelectionResult.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity == null) return NotFound($"选股批次 {id} 不存在");

        try
        {
            var svc = HttpContext.RequestServices.GetRequiredService<AIStock.Selection.TradeCandidateService>();
            var n = await svc.PopulateFromBatchesAsync(new[] { id }, ct);
            return Ok(new { success = true, added = n });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "入池失败 batch={Id}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ============ 配置中心 ============

    [HttpGet("config")]
    public async Task<ActionResult<SelectionCriteria>> GetConfig([FromQuery] string? name, CancellationToken ct)
    {
        var criteria = await _config.GetActiveCriteriaAsync(string.IsNullOrWhiteSpace(name) ? SelectionConfigService.DefaultName : name, ct);
        return Ok(criteria);
    }

    [HttpGet("config/default")]
    public ActionResult<SelectionCriteria> GetDefaultConfig() => Ok(SelectionConfigService.GetDefaultCriteria());

    [HttpGet("config/list")]
    public async Task<ActionResult<List<SelectionConfigEntity>>> ListConfig(CancellationToken ct)
        => Ok(await _config.ListAsync(ct));

    [HttpPost("config")]
    public async Task<ActionResult<SelectionConfigEntity>> SaveConfig([FromBody] SaveConfigRequest req, CancellationToken ct)
    {
        if (req?.Criteria == null) return BadRequest("缺少 criteria");
        var saved = await _config.SaveAsync(
            req.Name ?? SelectionConfigService.DefaultName,
            req.Version ?? "v1.0",
            req.Criteria, req.Remark, req.Activate, ct);
        return Ok(saved);
    }

    [HttpPost("config/{id:long}/activate")]
    public async Task<ActionResult> ActivateConfig(long id, CancellationToken ct)
        => await _config.ActivateAsync(id, ct) ? Ok() : NotFound();

    [HttpDelete("config/{id:long}")]
    public async Task<ActionResult> DeleteConfig(long id, CancellationToken ct)
        => await _config.DeleteAsync(id, ct) ? Ok() : BadRequest("配置不存在或正在生效，无法删除");

    public class SaveConfigRequest
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Remark { get; set; }
        public bool Activate { get; set; }
        public SelectionCriteria? Criteria { get; set; }
    }

    // ============ 回测 ============

    /// <summary>
    /// 回测历史选股结果。entry: NextOpen(默认,T+1开盘) | SignalClose(信号日收盘)。
    /// 新增 exitPreset / exitRules 参数支持高级出场规则。
    /// </summary>
    [HttpGet("backtest")]
    public async Task<ActionResult<BacktestReport>> Backtest(
        [FromQuery] int holdDays = 5, [FromQuery] string entry = "NextOpen",
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] bool tradability = true, [FromQuery] decimal friction = 0.3m,
        [FromQuery] decimal stopLoss = 0m, [FromQuery] decimal takeProfit = 0m,
        [FromQuery] string? exitPreset = null, [FromQuery] string? exitRules = null,
        [FromQuery] bool rejectBreakdown = false, [FromQuery] decimal ma5SlopePct = 1m,
        CancellationToken ct = default)
    {
        var config = new BacktestConfig
        {
            HoldDays = holdDays,
            Entry = string.Equals(entry, "SignalClose", StringComparison.OrdinalIgnoreCase)
                ? BacktestEntryTiming.SignalClose : BacktestEntryTiming.NextOpen,
            ApplyTradability = tradability,
            FrictionPct = friction,
            StopLossPct = stopLoss,
            TakeProfitPct = takeProfit,
            ExitPreset = exitPreset,
            RejectBreakdown = rejectBreakdown,
            Ma5DownSlopeMaxPct = ma5SlopePct,
        };

        // 解析高级出场规则 JSON
        if (!string.IsNullOrWhiteSpace(exitRules))
        {
            try
            {
                config.ExitRules = JsonSerializer.Deserialize<List<ExitRule>>(exitRules);
            }
            catch { /* 解析失败时忽略，回退到旧字段 */ }
        }

        // 用户未配置任何出场规则时，兜底使用「均衡默认」预设（不覆盖用户显式配置）
        ApplyDefaultExitPresetIfEmpty(config, stopLoss, takeProfit);

        DateTime? f = DateTime.TryParse(from, out var fd) ? fd : null;
        DateTime? t = DateTime.TryParse(to, out var td) ? td : null;
        var report = await _backtest.BacktestHistoryAsync(config, f, t, ct);
        return Ok(report);
    }

    /// <summary>
    /// 参数回放回测：用给定参数在 [from,to] 历史快照上逐日重跑选股并回测。
    /// 新增 exitPreset / exitRules 参数。
    /// </summary>
    [HttpPost("backtest/replay")]
    public async Task<ActionResult<BacktestReport>> ReplayBacktest(
        [FromBody] SelectionCriteria? criteria,
        [FromQuery] string? strategy, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int holdDays = 5, [FromQuery] string entry = "NextOpen",
        [FromQuery] bool tradability = true, [FromQuery] decimal friction = 0.3m,
        [FromQuery] decimal stopLoss = 0m, [FromQuery] decimal takeProfit = 0m,
        [FromQuery] string? exitPreset = null, [FromQuery] string? exitRules = null,
        [FromQuery] bool rejectBreakdown = false, [FromQuery] decimal ma5SlopePct = 1m,
        CancellationToken ct = default)
    {
        var c = criteria ?? await _config.GetActiveCriteriaAsync(
            string.IsNullOrWhiteSpace(strategy) ? SelectionConfigService.DefaultName : strategy, ct);
        var toD = DateTime.TryParse(to, out var td) ? td : DateTime.Today;
        var fromD = DateTime.TryParse(from, out var fd) ? fd : toD.AddDays(-30);

        var config = new BacktestConfig
        {
            HoldDays = holdDays,
            Entry = string.Equals(entry, "SignalClose", StringComparison.OrdinalIgnoreCase)
                ? BacktestEntryTiming.SignalClose : BacktestEntryTiming.NextOpen,
            ApplyTradability = tradability,
            FrictionPct = friction,
            StopLossPct = stopLoss,
            TakeProfitPct = takeProfit,
            ExitPreset = exitPreset,
            RejectBreakdown = rejectBreakdown,
            Ma5DownSlopeMaxPct = ma5SlopePct,
        };

        // 解析高级出场规则 JSON
        if (!string.IsNullOrWhiteSpace(exitRules))
        {
            try
            {
                config.ExitRules = JsonSerializer.Deserialize<List<ExitRule>>(exitRules);
            }
            catch { }
        }

        // 用户未配置任何出场规则时，兜底使用「均衡默认」预设（不覆盖用户显式配置）
        ApplyDefaultExitPresetIfEmpty(config, stopLoss, takeProfit);

        var report = await _replay.BacktestParamsAsync(strategy, c, fromD, toD, config, ct);
        return Ok(report);
    }

    /// <summary>
    /// 用户完全没配置出场规则（无 preset / 无 exitRules / 无止损止盈）时，兜底填「均衡默认」预设，
    /// 避免出场只剩裸持有到期。不覆盖任何用户显式配置。
    /// </summary>
    private static void ApplyDefaultExitPresetIfEmpty(BacktestConfig config, decimal stopLoss, decimal takeProfit)
    {
        if (string.IsNullOrWhiteSpace(config.ExitPreset)
            && (config.ExitRules == null || config.ExitRules.Count == 0)
            && stopLoss == 0m && takeProfit == 0m)
        {
            config.ExitPreset = "default";
        }
    }

    [HttpGet("activity")]
    public async Task<ActionResult> Activity(CancellationToken ct)
    {
        var pool = await _selection.ScreenActivityAsync(null, ct);
        var dto = pool.Select(h => new
        {
            h.Snapshot.Code,
            h.Snapshot.Name,
            h.Snapshot.ChangePercent,
            h.Snapshot.VolumeRatio,
            h.Snapshot.MainNetInflow,
            ActivityScore = h.ActivityScore,
            Features = h.Features
        });
        return Ok(dto);
    }

    // ============ 新增：高级回测接口 ============

    /// <summary>滚动窗口回测（Phase 6.1）。</summary>
    [HttpGet("backtest/rolling")]
    public async Task<ActionResult<List<object>>> RollingBacktest(
        [FromQuery] int windowDays = 60, [FromQuery] int stepDays = 20,
        [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] int holdDays = 5, [FromQuery] string entry = "NextOpen",
        CancellationToken ct = default)
    {
        var fromD = DateTime.TryParse(from, out var fd) ? fd : DateTime.Today.AddYears(-1);
        var toD = DateTime.TryParse(to, out var td) ? td : DateTime.Today;
        var results = new List<object>();

        var currentFrom = fromD;
        while (currentFrom.AddDays(windowDays) <= toD)
        {
            var currentTo = currentFrom.AddDays(windowDays);
            var config = new BacktestConfig
            {
                HoldDays = holdDays,
                Entry = string.Equals(entry, "SignalClose", StringComparison.OrdinalIgnoreCase)
                    ? BacktestEntryTiming.SignalClose : BacktestEntryTiming.NextOpen,
            };

            try
            {
                var report = await _backtest.BacktestHistoryAsync(config, currentFrom, currentTo, ct);
                results.Add(new
                {
                    from = currentFrom.ToString("yyyy-MM-dd"),
                    to = currentTo.ToString("yyyy-MM-dd"),
                    winRate = report.WinRatePct,
                    avgReturn = report.AvgReturnPct,
                    sharpe = report.SharpeRatio,
                    trades = report.ExecutedTrades,
                    profitFactor = report.ProfitFactor,
                });
            }
            catch { /* skip collapsed windows */ }

            currentFrom = currentFrom.AddDays(stepDays);
        }

        return Ok(results);
    }

    /// <summary>参数网格搜索回测（Phase 6.2）。</summary>
    [HttpPost("backtest/grid")]
    public async Task<ActionResult<List<object>>> GridBacktest(
        [FromBody] GridBacktestRequest request, CancellationToken ct = default)
    {
        var results = new List<object>();
        var holdDaysList = request.HoldDays?.Length > 0 ? request.HoldDays : new[] { 5 };
        var stopLossList = request.StopLossPct?.Length > 0 ? request.StopLossPct : new[] { 0m };
        var takeProfitList = request.TakeProfitPct?.Length > 0 ? request.TakeProfitPct : new[] { 0m };
        var presets = request.ExitPresets?.Length > 0 ? request.ExitPresets : new[] { (string?)null };

        var fromD = DateTime.TryParse(request.From, out var fd) ? fd : DateTime.Today.AddMonths(-3);
        var toD = DateTime.TryParse(request.To, out var td) ? td : DateTime.Today;

        foreach (var hd in holdDaysList)
            foreach (var sl in stopLossList)
                foreach (var tp in takeProfitList)
                    foreach (var preset in presets)
                    {
                        var config = new BacktestConfig
                        {
                            HoldDays = hd,
                            StopLossPct = sl,
                            TakeProfitPct = tp,
                            ExitPreset = preset,
                        };
                        try
                        {
                            var report = await _backtest.BacktestHistoryAsync(config, fromD, toD, ct);
                            results.Add(new
                            {
                                holdDays = hd,
                                stopLoss = sl,
                                takeProfit = tp,
                                exitPreset = preset,
                                winRate = report.WinRatePct,
                                avgReturn = report.AvgReturnPct,
                                sharpe = report.SharpeRatio,
                                profitFactor = report.ProfitFactor,
                                trades = report.ExecutedTrades,
                                maxDrawdown = report.MaxDrawdownPct,
                            });
                        }
                        catch { }
                    }

        return Ok(results);
    }

    public class GridBacktestRequest
    {
        public int[]? HoldDays { get; set; }
        public decimal[]? StopLossPct { get; set; }
        public decimal[]? TakeProfitPct { get; set; }
        public string[]? ExitPresets { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
    }
}
