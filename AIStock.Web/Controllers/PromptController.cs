using AIStock.Prompt;
using AIStock.Prompt.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIStock.Web.Controllers;

/// <summary>
/// Prompt模板管理
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PromptController : ControllerBase
{
    private readonly IPromptRegistry _promptRegistry;
    private readonly ILogger<PromptController> _logger;

    public PromptController(IPromptRegistry promptRegistry, ILogger<PromptController> logger)
    {
        _promptRegistry = promptRegistry;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有Prompt列表
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListPrompts([FromQuery] string? category)
    {
        var prompts = await _promptRegistry.ListPromptsAsync(category);
        return Ok(prompts);
    }

    /// <summary>
    /// 获取指定Prompt详情
    /// </summary>
    [HttpGet("{name}")]
    public async Task<IActionResult> GetPrompt(string name, [FromQuery] string version = "latest")
    {
        var template = await _promptRegistry.GetPromptAsync(name, version);
        if (template == null)
            return NotFound(new { error = $"Prompt not found: {name}" });

        return Ok(template);
    }

    /// <summary>
    /// 创建或更新Prompt模板（name+version唯一）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SavePrompt([FromBody] PromptTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
            return BadRequest(new { error = "Name is required" });
        if (string.IsNullOrWhiteSpace(template.Version))
            return BadRequest(new { error = "Version is required" });
        if (string.IsNullOrWhiteSpace(template.UserPrompt))
            return BadRequest(new { error = "UserPrompt is required" });

        try
        {
            await _promptRegistry.SavePromptAsync(template);
            return Ok(new { message = "Prompt saved", name = template.Name, version = template.Version });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save prompt: {Name}:{Version}", template.Name, template.Version);
            return StatusCode(500, new { error = "Save failed", detail = ex.Message });
        }
    }

    /// <summary>
    /// 删除Prompt
    /// </summary>
    [HttpDelete("{name}")]
    public async Task<IActionResult> DeletePrompt(string name, [FromQuery] string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return BadRequest(new { error = "Version is required" });

        var deleted = await _promptRegistry.DeletePromptAsync(name, version);
        if (!deleted)
            return NotFound(new { error = $"Prompt not found: {name}:{version}" });

        return Ok(new { message = $"Prompt {name}:{version} deleted" });
    }

    /// <summary>
    /// 重新加载缓存
    /// </summary>
    [HttpPost("reload")]
    public async Task<IActionResult> Reload()
    {
        await _promptRegistry.ReloadAsync();
        return Ok(new { message = "Prompts reloaded" });
    }

    /// <summary>
    /// 初始化默认Prompt数据（仅当数据库为空时写入）
    /// </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        try
        {
            await _promptRegistry.SeedDefaultPromptsAsync();
            return Ok(new { message = "Seed completed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seed failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
