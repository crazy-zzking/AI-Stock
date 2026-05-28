using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.EventEngine.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 事件管理
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private readonly EventEngineService _eventEngine;
    private readonly IEventExtractor _eventExtractor;
    private readonly ISentimentAnalyzer _sentimentAnalyzer;
    private readonly IIntensityScorer _intensityScorer;
    private readonly ICredibilityAnalyzer _credibilityAnalyzer;
    private readonly ISpreadAnalyzer _spreadAnalyzer;
    private readonly ILogger<EventController> _logger;

    public EventController(
        EventEngineService eventEngine,
        IEventExtractor eventExtractor,
        ISentimentAnalyzer sentimentAnalyzer,
        IIntensityScorer intensityScorer,
        ICredibilityAnalyzer credibilityAnalyzer,
        ISpreadAnalyzer spreadAnalyzer,
        ILogger<EventController> logger)
    {
        _eventEngine = eventEngine;
        _eventExtractor = eventExtractor;
        _sentimentAnalyzer = sentimentAnalyzer;
        _intensityScorer = intensityScorer;
        _credibilityAnalyzer = credibilityAnalyzer;
        _spreadAnalyzer = spreadAnalyzer;
        _logger = logger;
    }

    /// <summary>
    /// 获取最近事件
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRecentEvents([FromQuery] int count = 50, [FromQuery] string? eventType = null)
    {
        var events = await _eventEngine.GetRecentEventsAsync(count, eventType);
        return Ok(events);
    }

    /// <summary>
    /// 获取事件详情
    /// </summary>
    [HttpGet("{eventId}")]
    public async Task<IActionResult> GetEvent(long eventId)
    {
        var eventRecord = await _eventEngine.GetEventAsync(eventId);
        if (eventRecord == null)
            return NotFound(new { error = $"Event not found: {eventId}" });

        return Ok(eventRecord);
    }

    /// <summary>
    /// 搜索事件
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchEvents([FromQuery] string keyword, [FromQuery] int count = 50)
    {
        if (string.IsNullOrEmpty(keyword))
            return BadRequest(new { error = "Keyword is required" });

        var events = await _eventEngine.SearchEventsAsync(keyword, count);
        return Ok(events);
    }

    /// <summary>
    /// 获取事件统计
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var statistics = await _eventEngine.GetStatisticsAsync(startDate, endDate);
        return Ok(statistics);
    }

    /// <summary>
    /// 从文本中抽取事件
    /// </summary>
    [HttpPost("extract")]
    public async Task<IActionResult> ExtractEvent([FromBody] ExtractEventRequest request)
    {
        if (string.IsNullOrEmpty(request.Text))
            return BadRequest(new { error = "Text is required" });

        var eventData = await _eventExtractor.ExtractEventAsync(request.Text, request.EventType ?? "unknown");
        return Ok(eventData);
    }

    /// <summary>
    /// 分析文本情绪
    /// </summary>
    [HttpPost("sentiment")]
    public async Task<IActionResult> AnalyzeSentiment([FromBody] SentimentRequest request)
    {
        if (string.IsNullOrEmpty(request.Text))
            return BadRequest(new { error = "Text is required" });

        var result = await _sentimentAnalyzer.AnalyzeAsync(request.Text);
        return Ok(result);
    }

    /// <summary>
    /// 评估事件强度
    /// </summary>
    [HttpPost("intensity")]
    public async Task<IActionResult> ScoreIntensity([FromBody] EventData eventData)
    {
        var score = await _intensityScorer.ScoreAsync(eventData);
        return Ok(score);
    }

    /// <summary>
    /// 真假识别
    /// </summary>
    [HttpPost("credibility")]
    public async Task<IActionResult> AnalyzeCredibility([FromBody] EventData eventData)
    {
        var result = await _credibilityAnalyzer.AnalyzeAsync(eventData);
        return Ok(result);
    }

    /// <summary>
    /// 传播链分析
    /// </summary>
    [HttpGet("spread")]
    public async Task<IActionResult> AnalyzeSpread([FromQuery] string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
            return BadRequest(new { error = "Keyword is required" });

        var result = await _spreadAnalyzer.AnalyzeSpreadAsync(keyword);
        return Ok(result);
    }
}

/// <summary>
/// 事件抽取请求
/// </summary>
public class ExtractEventRequest
{
    /// <summary>
    /// 文本内容
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 事件类型
    /// </summary>
    public string? EventType { get; set; }
}

/// <summary>
/// 情绪分析请求
/// </summary>
public class SentimentRequest
{
    /// <summary>
    /// 文本内容
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
