using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Backtest;
using Microsoft.AspNetCore.Mvc;

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
        return Ok(list.Select(s => new { s.Key, s.Name, s.Description, s.PreferredRegime }));
    }

    /// <summary>可选 K 线形态目录（key/name），供"K线形态"策略在选股时自选形态。</summary>
    [HttpGet("patterns")]
    public ActionResult Patterns()
        => Ok(CandlePatternAnalyzer.PatternKeys.Select(k => new { key = k, name = CandlePatternAnalyzer.DisplayName(k) }));

    /// <summary>按自定义条件实时选股（不落库，调试/试参用）。strategy 选策略(默认 lowdip)；body 为空则用该策略生效配置。</summary>
    [HttpPost("screen")]
    public async Task<ActionResult<List<StockSelectionResult>>> Screen(
        [FromBody] SelectionCriteria? criteria, [FromQuery] string? strategy, CancellationToken ct)
    {
        var results = await _selection.SelectAsync(criteria, strategy, ct);
        return Ok(results);
    }

    /// <summary>
    /// 返回当日已冻结的选股结果（供面板展示）。当日首次访问会生成并落库，
    /// 之后盘中刷新返回同一份、结果不跳动。需重新选股请调 POST /run。
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<List<StockSelectionResult>>> Latest([FromQuery] int topN = 5, CancellationToken ct = default)
    {
        var results = await _selection.GetOrCreateLatestAsync(topN, ct);
        return Ok(results);
    }

    /// <summary>重新选股并追加一条历史记录（不覆盖），返回本次结果。strategy 选策略；body 为空则用该策略生效配置。</summary>
    [HttpPost("run")]
    public async Task<ActionResult<List<StockSelectionResult>>> Run(
        [FromBody] SelectionCriteria? criteria, [FromQuery] string? strategy, CancellationToken ct)
    {
        var results = await _selection.RunAndSaveAsync(criteria, strategy, ct);
        return Ok(results);
    }

    /// <summary>导入外部选股结果（历史未入库的批次）。date 为该批选股所基于的交易日(yyyy-MM-dd)。</summary>
    [HttpPost("import")]
    public async Task<ActionResult> Import([FromBody] List<StockSelectionResult>? picks, [FromQuery] string? date, CancellationToken ct = default)
    {
        if (picks == null || picks.Count == 0) return BadRequest("无选股数据");
        var d = DateTime.TryParse(date, out var parsed) ? parsed : DateTime.Today.AddDays(-1);
        var n = await _selection.ImportAsync(picks, d, ct);
        return Ok(new { imported = n, tradingDate = d.Date.ToString("yyyy-MM-dd") });
    }

    /// <summary>选股历史记录列表（元信息，按选股时间倒序）。</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<SelectionHistoryItem>>> History([FromQuery] int take = 30, CancellationToken ct = default)
    {
        var items = await _selection.GetHistoryAsync(take, ct);
        return Ok(items);
    }

    /// <summary>按 id 取某次选股的完整结果。</summary>
    [HttpGet("history/{id:long}")]
    public async Task<ActionResult<List<StockSelectionResult>>> HistoryDetail(long id, CancellationToken ct)
    {
        var results = await _selection.GetByIdAsync(id, ct);
        return Ok(results);
    }

    /// <summary>某批选股的选后表现：次日/至今涨跌、选中后最高涨幅与最低跌幅（按日K）。</summary>
    [HttpGet("history/{id:long}/performance")]
    public async Task<ActionResult<SelectionPerformance>> HistoryPerformance(long id, CancellationToken ct)
    {
        var perf = await _selection.GetPerformanceAsync(id, ct);
        return perf == null ? NotFound() : Ok(perf);
    }

    // ============ 配置中心（版本化的选股条件 + 权重）============

    /// <summary>当前生效的选股条件（含权重）。无配置时返回代码默认值。</summary>
    [HttpGet("config")]
    public async Task<ActionResult<SelectionCriteria>> GetConfig([FromQuery] string? name, CancellationToken ct)
    {
        var criteria = await _config.GetActiveCriteriaAsync(string.IsNullOrWhiteSpace(name) ? SelectionConfigService.DefaultName : name, ct);
        return Ok(criteria);
    }

    /// <summary>代码内置默认条件（前端"恢复默认"用）。</summary>
    [HttpGet("config/default")]
    public ActionResult<SelectionCriteria> GetDefaultConfig() => Ok(SelectionConfigService.GetDefaultCriteria());

    /// <summary>列出所有配置版本（含 JSON，供查看/对比）。</summary>
    [HttpGet("config/list")]
    public async Task<ActionResult<List<SelectionConfigEntity>>> ListConfig(CancellationToken ct)
        => Ok(await _config.ListAsync(ct));

    /// <summary>保存为新版本（可选同时激活）。</summary>
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

    /// <summary>激活指定配置版本。</summary>
    [HttpPost("config/{id:long}/activate")]
    public async Task<ActionResult> ActivateConfig(long id, CancellationToken ct)
        => await _config.ActivateAsync(id, ct) ? Ok() : NotFound();

    /// <summary>删除配置版本（生效中的不可删）。</summary>
    [HttpDelete("config/{id:long}")]
    public async Task<ActionResult> DeleteConfig(long id, CancellationToken ct)
        => await _config.DeleteAsync(id, ct) ? Ok() : BadRequest("配置不存在或正在生效，无法删除");

    /// <summary>保存配置请求体。</summary>
    public class SaveConfigRequest
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Remark { get; set; }
        public bool Activate { get; set; }
        public SelectionCriteria? Criteria { get; set; }
    }

    /// <summary>
    /// 回测历史选股结果：把 [from,to] 区间内已落库的选股记录当信号，按持有期/买点用 K 线统计
    /// 胜率/平均收益/盈亏比/回撤。entry: NextOpen(默认,T+1开盘) | SignalClose(信号日收盘)。
    /// </summary>
    [HttpGet("backtest")]
    public async Task<ActionResult<BacktestReport>> Backtest(
        [FromQuery] int holdDays = 5, [FromQuery] string entry = "NextOpen",
        [FromQuery] string? from = null, [FromQuery] string? to = null, CancellationToken ct = default)
    {
        var config = new BacktestConfig
        {
            HoldDays = holdDays,
            Entry = string.Equals(entry, "SignalClose", StringComparison.OrdinalIgnoreCase)
                ? BacktestEntryTiming.SignalClose : BacktestEntryTiming.NextOpen,
        };
        DateTime? f = DateTime.TryParse(from, out var fd) ? fd : null;
        DateTime? t = DateTime.TryParse(to, out var td) ? td : null;
        var report = await _backtest.BacktestHistoryAsync(config, f, t, ct);
        return Ok(report);
    }

    /// <summary>
    /// 参数回放回测：用给定参数(body=criteria，为空则用该策略生效配置)在 [from,to] 历史快照上
    /// 逐日重跑选股并回测。供大模型自动调参对比。from/to 缺省=最近30天。
    /// </summary>
    [HttpPost("backtest/replay")]
    public async Task<ActionResult<BacktestReport>> ReplayBacktest(
        [FromBody] SelectionCriteria? criteria,
        [FromQuery] string? strategy, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int holdDays = 5, [FromQuery] string entry = "NextOpen", CancellationToken ct = default)
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
        };
        var report = await _replay.BacktestParamsAsync(strategy, c, fromD, toD, config, ct);
        return Ok(report);
    }

    /// <summary>仅返回第一级活跃度粗筛池（调试/观察用）。</summary>
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
}
