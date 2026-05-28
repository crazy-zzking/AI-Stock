using System.ComponentModel.DataAnnotations;
using AIStock.Core.Interfaces;
using AIStock.Orchestrator;
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
    private readonly AutonomousDecisionSystem _decisionSystem;
    private readonly ILogger<OrchestratorController> _logger;

    public OrchestratorController(
        IAgentOrchestrator orchestrator,
        ResearchAgent researchAgent,
        AlphaAgent alphaAgent,
        RiskAgent riskAgent,
        AutonomousDecisionSystem decisionSystem,
        ILogger<OrchestratorController> logger)
    {
        _orchestrator = orchestrator;
        _researchAgent = researchAgent;
        _alphaAgent = alphaAgent;
        _riskAgent = riskAgent;
        _decisionSystem = decisionSystem;
        _logger = logger;

        // Agent注册已移至AgentOrchestrator构造函数，通过DI自动完成
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

    /// <summary>
    /// 自主决策
    /// </summary>
    [HttpPost("decision")]
    public async Task<ActionResult<DecisionResult>> MakeDecision([FromBody] DecisionRequest request)
    {
        var result = await _decisionSystem.MakeDecisionAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 批量决策
    /// </summary>
    [HttpPost("decision/batch")]
    public async Task<ActionResult<List<DecisionResult>>> MakeBatchDecision([FromBody] BatchDecisionRequest request)
    {
        var results = await _decisionSystem.MakeBatchDecisionAsync(request.Codes, request.TotalCapital);
        return Ok(results);
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
    [Required(ErrorMessage = "股票代码不能为空")]
    [StringLength(10, MinimumLength = 5, ErrorMessage = "股票代码长度必须在5-10之间")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// 批量决策请求
/// </summary>
public class BatchDecisionRequest
{
    /// <summary>
    /// 股票代码列表
    /// </summary>
    [Required(ErrorMessage = "股票代码列表不能为空")]
    [MinLength(1, ErrorMessage = "至少需要一个股票代码")]
    public List<string> Codes { get; set; } = new();

    /// <summary>
    /// 总资金
    /// </summary>
    [Range(1, double.MaxValue, ErrorMessage = "总资金必须大于0")]
    public decimal TotalCapital { get; set; }
}
