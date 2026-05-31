using AIStock.Core.Models;
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
    private readonly ILogger<SelectionController> _logger;

    public SelectionController(StockSelectionService selection, ILogger<SelectionController> logger)
    {
        _selection = selection;
        _logger = logger;
    }

    /// <summary>按自定义条件选股，返回 TOP-N。</summary>
    [HttpPost("screen")]
    public async Task<ActionResult<List<StockSelectionResult>>> Screen([FromBody] SelectionCriteria? criteria, CancellationToken ct)
    {
        var results = await _selection.SelectAsync(criteria ?? new SelectionCriteria(), ct);
        return Ok(results);
    }

    /// <summary>按默认条件返回最新一期选股结果（供面板展示）。</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<List<StockSelectionResult>>> Latest([FromQuery] int topN = 5, CancellationToken ct = default)
    {
        var results = await _selection.SelectAsync(new SelectionCriteria { TopN = topN }, ct);
        return Ok(results);
    }

    /// <summary>仅返回第一级活跃度粗筛池（调试/观察用）。</summary>
    [HttpGet("activity")]
    public async Task<ActionResult> Activity(CancellationToken ct)
    {
        var pool = await _selection.ScreenActivityAsync(new SelectionCriteria(), ct);
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
