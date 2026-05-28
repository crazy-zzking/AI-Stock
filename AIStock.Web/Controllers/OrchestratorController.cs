using AIStock.Core.Interfaces;
using AIStock.Orchestrator.Agents;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// Agent编排API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrchestratorController : ControllerBase
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ResearchAgent _researchAgent;
    private readonly AlphaAgent _alphaAgent;
    private readonly RiskAgent _riskAgent;
    private readonly ILogger<OrchestratorController> _logger;

    public OrchestratorController(
        IAgentOrchestrator orchestrator,
        ResearchAgent researchAgent,
        AlphaAgent alphaAgent,
        RiskAgent riskAgent,
        ILogger<OrchestratorController> logger)
    {
        _orchestrator = orchestrator;
        _researchAgent = researchAgent;
        _alphaAgent = alphaAgent;
        _riskAgent = riskAgent;

        _orchestrator.RegisterAgent(_researchAgent);
        _orchestrator.RegisterAgent(_alphaAgent);
        _orchestrator.RegisterAgent(_riskAgent);

        _logger = logger;
    }

    /// <summary>
    /// 获取所有Agent状态
    /// </summary>
    [HttpGet("agents")]
    public async Task<ActionResult<List<AgentStatus>>> GetAgents()
    {
        var statuses = await _orchestrator.GetAllAgentStatusAsync();
        return Ok(statuses);
    }

    /// <summary>
    /// 执行股票分析
    /// </summary>
    [HttpPost("analyze")]
    public async Task<ActionResult<AgentResult>> AnalyzeStock([FromBody] AnalyzeRequest request)
    {
        var task = new AgentTask
        {
            TaskType = "analyze-stock",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = request.Code
            }
        };

        var result = await _researchAgent.ExecuteAsync(task);
        return Ok(result);
    }

    /// <summary>
    /// 生成交易信号
    /// </summary>
    [HttpPost("signal")]
    public async Task<ActionResult<AgentResult>> GenerateSignal([FromBody] AnalyzeRequest request)
    {
        var task = new AgentTask
        {
            TaskType = "generate-signal",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = request.Code
            }
        };

        var result = await _alphaAgent.ExecuteAsync(task);
        return Ok(result);
    }

    /// <summary>
    /// 执行工作流
    /// </summary>
    [HttpPost("workflow")]
    public async Task<ActionResult<WorkflowResult>> ExecuteWorkflow([FromBody] WorkflowDefinition workflow)
    {
        var result = await _orchestrator.ExecuteWorkflowAsync(workflow);
        return Ok(result);
    }
}

/// <summary>
/// 分析请求
/// </summary>
public class AnalyzeRequest
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;
}
