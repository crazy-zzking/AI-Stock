using AIStock.Infrastructure.Database.Context;
using AIStock.Selection.Performance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Controllers;

/// <summary>
/// 选股信号前向绩效 API（策略记分板）。数据由 Worker 的 selection-performance 任务每日物化补算；
/// 这里只读 selection_performance 表，不触发计算。
/// </summary>
[ApiController]
[Route("api/selection/performance")]
public class SelectionPerformanceController : ControllerBase
{
    private readonly SelectionPerformanceService _svc;
    private readonly AIStockDbContext _db;

    public SelectionPerformanceController(SelectionPerformanceService svc, AIStockDbContext db)
    {
        _svc = svc;
        _db = db;
    }

    /// <summary>按策略聚合最近 N 天信号绩效（胜率/均收益/超额/盈亏比，T+1/3/5 三窗口）。regime 可选过滤（weak/neutral/strong）。</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<List<StrategyPerformanceSummary>>> Summary(
        [FromQuery] int days = 30, [FromQuery] string? regime = null, CancellationToken ct = default)
    {
        if (days <= 0 || days > 365) days = 30;
        return Ok(await _svc.GetSummaryAsync(days, regime, ct));
    }

    /// <summary>信号明细（可按策略过滤），按交易日倒序、同日按得分倒序。</summary>
    [HttpGet("details")]
    public async Task<ActionResult> Details(
        [FromQuery] string? strategy, [FromQuery] int days = 30, [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (days <= 0 || days > 365) days = 30;
        if (limit <= 0 || limit > 2000) limit = 500;

        var since = DateTime.Today.AddDays(-days);
        var q = _db.SelectionPerformance.Where(p => p.TradingDate >= since);
        if (!string.IsNullOrEmpty(strategy)) q = q.Where(p => p.Strategy == strategy);

        var rows = await q
            .OrderByDescending(p => p.TradingDate).ThenByDescending(p => p.Score)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>LLM 复评价值考核：按复评建议等级（Buy/Watch/Avoid）聚合前向绩效，验证复评判断力。</summary>
    [HttpGet("review-stats")]
    public async Task<ActionResult<List<ReviewPerformanceSummary>>> ReviewStats(
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (days <= 0 || days > 365) days = 30;
        return Ok(await _svc.GetReviewStatsAsync(days, ct));
    }

    /// <summary>手动触发一次物化+补算（通常由 Worker 定时执行，这里用于补数/调试）。</summary>
    [HttpPost("sync")]
    public async Task<ActionResult> Sync(CancellationToken ct = default)
    {
        var (created, updated, finalized) = await _svc.SyncAsync(ct);
        return Ok(new { created, updated, finalized });
    }
}
