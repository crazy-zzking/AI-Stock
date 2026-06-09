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
    /// 获取所有模型配置（含禁用）
    /// </summary>
    [HttpGet("models")]
    public async Task<IActionResult> GetModels()
    {
        var models = await _llmService.GetAllModelsAsync();
        return Ok(models);
    }

    /// <summary>
    /// 获取所有可用模型（仅启用）
    /// </summary>
    [HttpGet("models/available")]
    public async Task<IActionResult> GetAvailableModels()
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
    /// 添加模型配置
    /// </summary>
    [HttpPost("models")]
    public async Task<IActionResult> AddModel([FromBody] LLMConfig config)
    {
        if (string.IsNullOrEmpty(config.Id))
            return BadRequest(new { error = "Model Id is required" });
        if (string.IsNullOrEmpty(config.Name))
            return BadRequest(new { error = "Model Name is required" });

        try
        {
            var result = await _llmService.AddModelConfigAsync(config);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add model config: {ModelId}", config.Id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 更新模型配置
    /// </summary>
    [HttpPut("models/{modelId}")]
    public async Task<IActionResult> UpdateModel(string modelId, [FromBody] LLMConfig config)
    {
        if (modelId != config.Id)
            return BadRequest(new { error = "ModelId mismatch" });

        try
        {
            var result = await _llmService.UpdateModelConfigAsync(config);
            if (result == null)
                return NotFound(new { error = $"Model not found: {modelId}" });
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update model config: {ModelId}", config.Id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 删除模型配置
    /// </summary>
    [HttpDelete("models/{modelId}")]
    public async Task<IActionResult> DeleteModel(string modelId)
    {
        try
        {
            var result = await _llmService.DeleteModelConfigAsync(modelId);
            if (!result)
                return NotFound(new { error = $"Model not found: {modelId}" });
            return Ok(new { message = $"Model {modelId} deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete model config: {ModelId}", modelId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 查询 DeepSeek 账户余额（实时，不入库）
    /// </summary>
    [HttpGet("models/{modelId}/balance")]
    public async Task<IActionResult> GetBalance(string modelId)
    {
        var balance = await _llmService.GetDeepSeekBalanceAsync(modelId);
        return Ok(balance);
    }

    /// <summary>
    /// 测试指定模型连通性（含禁用模型，真实调用外部 API）
    /// </summary>
    [HttpPost("models/{modelId}/test")]
    public async Task<IActionResult> TestModel(string modelId, [FromBody] LLMTestRequest? request)
    {
        var prompt = request?.UserPrompt;
        if (string.IsNullOrWhiteSpace(prompt))
            prompt = "你好，请用一句话简单自我介绍。";

        var response = await _llmService.TestModelAsync(modelId, prompt, request?.SystemPrompt);
        return Ok(response);
    }

    /// <summary>
    /// 测试给定配置连通性（不读库，用表单当前值；含未保存修改）
    /// </summary>
    [HttpPost("models/test")]
    public async Task<IActionResult> TestConfig([FromBody] LLMTestConfigRequest request)
    {
        if (request?.Config == null)
            return BadRequest(new { error = "Config is required" });

        var prompt = request.UserPrompt;
        if (string.IsNullOrWhiteSpace(prompt))
            prompt = "你好，请用一句话简单自我介绍。";

        var response = await _llmService.TestConfigAsync(request.Config, prompt, request.SystemPrompt);
        return Ok(response);
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
/// LLM 模型测试请求
/// </summary>
public class LLMTestRequest
{
    /// <summary>
    /// 用户提示词（为空则用默认自我介绍提示词）
    /// </summary>
    public string? UserPrompt { get; set; }

    /// <summary>
    /// 系统提示词（可选）
    /// </summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// LLM 配置测试请求（用表单当前值，不读库）
/// </summary>
public class LLMTestConfigRequest
{
    /// <summary>
    /// 待测试的模型配置
    /// </summary>
    public LLMConfig? Config { get; set; }

    /// <summary>
    /// 用户提示词（为空则用默认自我介绍提示词）
    /// </summary>
    public string? UserPrompt { get; set; }

    /// <summary>
    /// 系统提示词（可选）
    /// </summary>
    public string? SystemPrompt { get; set; }
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
