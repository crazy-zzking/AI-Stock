using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
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
    private readonly ILogger<SelectionController> _logger;

    public SelectionController(StockSelectionService selection, SelectionConfigService config, ILogger<SelectionController> logger)
    {
        _selection = selection;
        _config = config;
        _logger = logger;
    }

    /// <summary>按自定义条件实时选股（不落库，调试/试参用）。body 为空则用配置中心生效配置。</summary>
    [HttpPost("screen")]
    public async Task<ActionResult<List<StockSelectionResult>>> Screen([FromBody] SelectionCriteria? criteria, CancellationToken ct)
    {
        var results = await _selection.SelectAsync(criteria, ct);
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

    /// <summary>重新选股并追加一条历史记录（不覆盖），返回本次结果。body 为空则用生效配置。</summary>
    [HttpPost("run")]
    public async Task<ActionResult<List<StockSelectionResult>>> Run([FromBody] SelectionCriteria? criteria, CancellationToken ct)
    {
        var results = await _selection.RunAndSaveAsync(criteria, ct);
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
