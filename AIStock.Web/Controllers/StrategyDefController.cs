using AIStock.Selection;
using AIStock.Selection.Strategies;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 自建选股策略定义 CRUD —— 维护 strategy_definition 表（过滤口径 + 因子口径 + 惩罚口径）。
/// 注意：策略的权重/阈值仍由「选股配置中心」(/api/selection/config) 按 key 关联管理，本控制器只管"口径定义"。
/// </summary>
[ApiController]
[Route("api/strategy-def")]
public class StrategyDefController : ControllerBase
{
    private readonly StrategyDefinitionService _service;
    private readonly ILogger<StrategyDefController> _logger;

    public StrategyDefController(StrategyDefinitionService service, ILogger<StrategyDefController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>列出全部自建策略（含结构化定义、启用状态、时间）。</summary>
    [HttpGet]
    public async Task<ActionResult<List<StrategyDefDto>>> List(CancellationToken ct)
    {
        var entities = await _service.ListAsync(ct);
        var dtos = entities.Select(e => new StrategyDefDto
        {
            Id = e.Id,
            Enabled = e.Enabled,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            Definition = _service.ParseDefinition(e),
        }).ToList();
        return Ok(dtos);
    }

    /// <summary>三个内置策略的定义模板（"以内置为模板克隆"用）。</summary>
    [HttpGet("builtins")]
    public ActionResult<List<StrategyDefinition>> Builtins()
        => Ok(StrategyDefinitionService.Builtins());

    /// <summary>新建自建策略。</summary>
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] StrategyDefRequest? req, CancellationToken ct)
    {
        if (req?.Definition == null) return BadRequest("缺少 definition");
        try
        {
            var saved = await _service.CreateAsync(req.Definition, req.Enabled, ct);
            return Ok(new { saved.Id });
        }
        catch (StrategyDefValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>更新自建策略。</summary>
    [HttpPut("{id:long}")]
    public async Task<ActionResult> Update(long id, [FromBody] StrategyDefRequest? req, CancellationToken ct)
    {
        if (req?.Definition == null) return BadRequest("缺少 definition");
        try
        {
            var saved = await _service.UpdateAsync(id, req.Definition, req.Enabled, ct);
            return saved == null ? NotFound() : Ok(new { saved.Id });
        }
        catch (StrategyDefValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>删除自建策略。</summary>
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> Delete(long id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? Ok() : NotFound();

    /// <summary>自建策略请求体：结构化定义 + 启用开关。</summary>
    public class StrategyDefRequest
    {
        public bool Enabled { get; set; } = true;
        public StrategyDefinition? Definition { get; set; }
    }

    /// <summary>自建策略列表项。</summary>
    public class StrategyDefDto
    {
        public long Id { get; set; }
        public bool Enabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public StrategyDefinition Definition { get; set; } = new();
    }
}
