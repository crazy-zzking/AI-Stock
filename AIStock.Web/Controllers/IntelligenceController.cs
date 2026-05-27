using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.EventEngine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 情报分析
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IntelligenceController : ControllerBase
{
    private readonly IReportCollector _reportCollector;
    private readonly INewsCollector _newsCollector;
    private readonly IReportAnalyzer _reportAnalyzer;
    private readonly IPolicyAnalyzer _policyAnalyzer;
    private readonly EventEngineService _eventEngine;
    private readonly ILogger<IntelligenceController> _logger;

    public IntelligenceController(
        IReportCollector reportCollector,
        INewsCollector newsCollector,
        IReportAnalyzer reportAnalyzer,
        IPolicyAnalyzer policyAnalyzer,
        EventEngineService eventEngine,
        ILogger<IntelligenceController> logger)
    {
        _reportCollector = reportCollector;
        _newsCollector = newsCollector;
        _reportAnalyzer = reportAnalyzer;
        _policyAnalyzer = policyAnalyzer;
        _eventEngine = eventEngine;
        _logger = logger;
    }

    /// <summary>
    /// 采集最新研报
    /// </summary>
    [HttpGet("reports")]
    public async Task<IActionResult> CollectReports([FromQuery] int count = 20)
    {
        var reports = await _reportCollector.CollectReportsAsync(count);
        return Ok(reports);
    }

    /// <summary>
    /// 采集指定股票的研报
    /// </summary>
    [HttpGet("reports/{stockCode}")]
    public async Task<IActionResult> CollectReportsByStock(string stockCode, [FromQuery] int count = 10)
    {
        var reports = await _reportCollector.CollectReportsByStockAsync(stockCode, count);
        return Ok(reports);
    }

    /// <summary>
    /// 分析研报
    /// </summary>
    [HttpPost("reports/analyze")]
    public async Task<IActionResult> AnalyzeReport([FromBody] ReportData report)
    {
        var analysis = await _reportAnalyzer.AnalyzeAsync(report);
        return Ok(analysis);
    }

    /// <summary>
    /// 采集最新新闻
    /// </summary>
    [HttpGet("news")]
    public async Task<IActionResult> CollectNews([FromQuery] int count = 20)
    {
        var news = await _newsCollector.CollectLatestNewsAsync(count);
        return Ok(news);
    }

    /// <summary>
    /// 采集指定股票的新闻
    /// </summary>
    [HttpGet("news/{stockCode}")]
    public async Task<IActionResult> CollectNewsByStock(string stockCode, [FromQuery] int count = 10)
    {
        var news = await _newsCollector.CollectNewsByStockAsync(stockCode, count);
        return Ok(news);
    }

    /// <summary>
    /// 采集指定类别的新闻
    /// </summary>
    [HttpGet("news/category/{category}")]
    public async Task<IActionResult> CollectNewsByCategory(string category, [FromQuery] int count = 20)
    {
        var news = await _newsCollector.CollectNewsByCategoryAsync(category, count);
        return Ok(news);
    }

    /// <summary>
    /// 分析政策
    /// </summary>
    [HttpPost("policy/analyze")]
    public async Task<IActionResult> AnalyzePolicy([FromBody] PolicyAnalysisRequest request)
    {
        var analysis = await _policyAnalyzer.AnalyzeAsync(request.Title, request.Content);
        return Ok(analysis);
    }

    /// <summary>
    /// 处理并保存研报事件
    /// </summary>
    [HttpPost("reports/process")]
    public async Task<IActionResult> ProcessReport([FromBody] ReportData report)
    {
        var eventRecord = await _eventEngine.ProcessReportAsync(report);
        return Ok(eventRecord);
    }

    /// <summary>
    /// 处理并保存新闻事件
    /// </summary>
    [HttpPost("news/process")]
    public async Task<IActionResult> ProcessNews([FromBody] NewsData news)
    {
        var eventRecord = await _eventEngine.ProcessNewsAsync(news);
        return Ok(eventRecord);
    }

    /// <summary>
    /// 处理并保存政策事件
    /// </summary>
    [HttpPost("policy/process")]
    public async Task<IActionResult> ProcessPolicy([FromBody] PolicyAnalysisRequest request)
    {
        var eventRecord = await _eventEngine.ProcessPolicyAsync(request.Title, request.Content, request.Source);
        return Ok(eventRecord);
    }
}

/// <summary>
/// 政策分析请求
/// </summary>
public class PolicyAnalysisRequest
{
    /// <summary>
    /// 政策标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 政策内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 来源
    /// </summary>
    public string? Source { get; set; }
}
