using System.ComponentModel.DataAnnotations;
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
    private readonly IKnowledgeStarCollector _knowledgeStarCollector;
    private readonly IReportAnalyzer _reportAnalyzer;
    private readonly IPolicyAnalyzer _policyAnalyzer;
    private readonly IEssayAnalyzer _essayAnalyzer;
    private readonly EventEngineService _eventEngine;
    private readonly ILogger<IntelligenceController> _logger;

    public IntelligenceController(
        IReportCollector reportCollector,
        INewsCollector newsCollector,
        IKnowledgeStarCollector knowledgeStarCollector,
        IReportAnalyzer reportAnalyzer,
        IPolicyAnalyzer policyAnalyzer,
        IEssayAnalyzer essayAnalyzer,
        EventEngineService eventEngine,
        ILogger<IntelligenceController> logger)
    {
        _reportCollector = reportCollector;
        _newsCollector = newsCollector;
        _knowledgeStarCollector = knowledgeStarCollector;
        _reportAnalyzer = reportAnalyzer;
        _policyAnalyzer = policyAnalyzer;
        _essayAnalyzer = essayAnalyzer;
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

    #region 小作文分析

    /// <summary>
    /// 分析文本内容（小作文）
    /// </summary>
    [HttpPost("essay/analyze")]
    public async Task<IActionResult> AnalyzeEssay([FromBody] EssayAnalysisRequest request)
    {
        if (string.IsNullOrEmpty(request.Text))
            return BadRequest(new { error = "Text is required" });

        var result = await _essayAnalyzer.AnalyzeTextAsync(request.Text);
        return Ok(result);
    }

    /// <summary>
    /// 分析图片内容（OCR）
    /// </summary>
    [HttpPost("essay/analyze/image")]
    public async Task<IActionResult> AnalyzeEssayImage([FromBody] EssayImageAnalysisRequest request)
    {
        if (string.IsNullOrEmpty(request.ImageUrl))
            return BadRequest(new { error = "ImageUrl is required" });

        var result = await _essayAnalyzer.AnalyzeImageAsync(request.ImageUrl);
        return Ok(result);
    }

    /// <summary>
    /// 分析音频内容（ASR）
    /// </summary>
    [HttpPost("essay/analyze/audio")]
    public async Task<IActionResult> AnalyzeEssayAudio([FromBody] EssayAudioAnalysisRequest request)
    {
        if (string.IsNullOrEmpty(request.AudioUrl))
            return BadRequest(new { error = "AudioUrl is required" });

        var result = await _essayAnalyzer.AnalyzeAudioAsync(request.AudioUrl);
        return Ok(result);
    }

    #endregion

    #region 知识星球

    /// <summary>
    /// 获取知识星球最新内容
    /// </summary>
    [HttpGet("knowledge-star")]
    public async Task<IActionResult> GetKnowledgeStarContent([FromQuery] int count = 20)
    {
        var content = await _knowledgeStarCollector.GetLatestContentAsync(count);
        return Ok(content);
    }

    /// <summary>
    /// 获取指定知识星球的内容
    /// </summary>
    [HttpGet("knowledge-star/{groupId}")]
    public async Task<IActionResult> GetKnowledgeStarGroupContent(string groupId, [FromQuery] int count = 20)
    {
        var content = await _knowledgeStarCollector.GetGroupContentAsync(groupId, count);
        return Ok(content);
    }

    /// <summary>
    /// 搜索知识星球内容
    /// </summary>
    [HttpGet("knowledge-star/search")]
    public async Task<IActionResult> SearchKnowledgeStarContent([FromQuery] string keyword, [FromQuery] int count = 20)
    {
        if (string.IsNullOrEmpty(keyword))
            return BadRequest(new { error = "Keyword is required" });

        var content = await _knowledgeStarCollector.SearchContentAsync(keyword, count);
        return Ok(content);
    }

    #endregion
}

/// <summary>
/// 政策分析请求
/// </summary>
public class PolicyAnalysisRequest
{
    /// <summary>
    /// 政策标题
    /// </summary>
    [Required(ErrorMessage = "政策标题不能为空")]
    [StringLength(500, ErrorMessage = "标题长度不能超过500")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 政策内容
    /// </summary>
    [Required(ErrorMessage = "政策内容不能为空")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 来源
    /// </summary>
    public string? Source { get; set; }
}

public class EssayAnalysisRequest
{
    /// <summary>
    /// 文本内容
    /// </summary>
    [Required(ErrorMessage = "文本内容不能为空")]
    public string Text { get; set; } = string.Empty;
}

public class EssayImageAnalysisRequest
{
    /// <summary>
    /// 图片URL
    /// </summary>
    [Required(ErrorMessage = "图片URL不能为空")]
    [Url(ErrorMessage = "请输入有效的URL")]
    public string ImageUrl { get; set; } = string.Empty;
}

public class EssayAudioAnalysisRequest
{
    /// <summary>
    /// 音频URL
    /// </summary>
    [Required(ErrorMessage = "音频URL不能为空")]
    [Url(ErrorMessage = "请输入有效的URL")]
    public string AudioUrl { get; set; } = string.Empty;
}
