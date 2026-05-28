using System.ComponentModel.DataAnnotations;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// LLM模型管理
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LLMController : ControllerBase
{
    private readonly ILLMService _llmService;
    private readonly ILogger<LLMController> _logger;

    public LLMController(ILLMService llmService, ILogger<LLMController> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有可用模型
    /// </summary>
    [HttpGet("models")]
    public async Task<IActionResult> GetModels()
    {
        var models = await _llmService.GetAvailableModelsAsync();
        return Ok(models);
    }

    /// <summary>
    /// 获取指定模型配置
    /// </summary>
    [HttpGet("models/{modelId}")]
    public async Task<IActionResult> GetModel(string modelId)
    {
        var model = await _llmService.GetModelConfigAsync(modelId);
        if (model == null)
            return NotFound(new { error = $"Model not found: {modelId}" });

        return Ok(model);
    }

    /// <summary>
    /// 刷新模型配置缓存
    /// </summary>
    [HttpPost("models/refresh")]
    public async Task<IActionResult> RefreshModels()
    {
        await _llmService.RefreshModelConfigsAsync();
        return Ok(new { message = "Model configs refreshed" });
    }

    /// <summary>
    /// 发送LLM请求
    /// </summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] LLMRequest request)
    {
        if (string.IsNullOrEmpty(request.UserPrompt))
            return BadRequest(new { error = "UserPrompt is required" });

        var response = await _llmService.SendAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// 多模型对比
    /// </summary>
    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] LLMCompareRequest request)
    {
        if (string.IsNullOrEmpty(request.Prompt))
            return BadRequest(new { error = "Prompt is required" });

        var llmRequest = new LLMRequest
        {
            SystemPrompt = request.SystemPrompt,
            UserPrompt = request.Prompt
        };

        var result = await _llmService.CompareAsync(llmRequest, request.ModelCount);
        return Ok(result);
    }
}

/// <summary>
/// LLM对比请求
/// </summary>
public class LLMCompareRequest
{
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 用户提示词
    /// </summary>
    [Required(ErrorMessage = "提示词不能为空")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// 对比模型数量
    /// </summary>
    [Range(1, 10, ErrorMessage = "对比模型数量必须在1-10之间")]
    public int ModelCount { get; set; } = 3;
}
