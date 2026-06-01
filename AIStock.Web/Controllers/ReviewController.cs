using AIStock.Core.Models;
using AIStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 每日复盘 API — 基于已有数据源分析当日领涨板块/个股及涨因、诊断数据完备性、给出复盘总结。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReviewController : ControllerBase
{
    private readonly DailyReviewService _review;

    public ReviewController(DailyReviewService review)
    {
        _review = review;
    }

    /// <summary>最近一次复盘（无则按最新交易日即时生成并落库）。</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<DailyReviewReport>> Latest(CancellationToken ct)
    {
        var report = await _review.GetOrCreateLatestAsync(ct);
        return report == null ? Ok(new DailyReviewReport()) : Ok(report);
    }

    /// <summary>指定交易日的复盘（yyyy-MM-dd，已落库才有）。</summary>
    [HttpGet("{date}")]
    public async Task<ActionResult<DailyReviewReport>> ByDate(string date, CancellationToken ct)
    {
        if (!DateTime.TryParse(date, out var d)) return BadRequest("日期格式应为 yyyy-MM-dd");
        var report = await _review.GetByDateAsync(d, ct);
        return report == null ? NotFound() : Ok(report);
    }

    /// <summary>重新生成复盘（覆盖当日，可指定交易日）。</summary>
    [HttpPost("run")]
    public async Task<ActionResult<DailyReviewReport>> Run([FromQuery] string? date, CancellationToken ct = default)
    {
        DateTime? d = DateTime.TryParse(date, out var parsed) ? parsed : null;
        var report = await _review.GenerateAndSaveAsync(d, ct);
        return report == null ? Ok(new DailyReviewReport()) : Ok(report);
    }

    /// <summary>复盘历史列表（元信息，按交易日倒序）。</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<DailyReviewSummaryItem>>> History([FromQuery] int take = 30, CancellationToken ct = default)
    {
        var items = await _review.GetHistoryAsync(take, ct);
        return Ok(items);
    }
}
