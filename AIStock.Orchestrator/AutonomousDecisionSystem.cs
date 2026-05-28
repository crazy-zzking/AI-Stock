using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator;

/// <summary>
/// 自主决策系统 - 自动分析市场并生成交易决策，风控通过后自动下单
/// </summary>
public class AutonomousDecisionSystem
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IOrderManager _orderManager;
    private readonly ILogger<AutonomousDecisionSystem> _logger;

    public AutonomousDecisionSystem(
        IAgentOrchestrator orchestrator,
        IDataProviderResolver dataProviderResolver,
        IOrderManager orderManager,
        ILogger<AutonomousDecisionSystem> logger)
    {
        _orchestrator = orchestrator;
        _dataProviderResolver = dataProviderResolver;
        _orderManager = orderManager;
        _logger = logger;
    }

    /// <summary>
    /// 执行自主决策流程
    /// </summary>
    public async Task<DecisionResult> MakeDecisionAsync(DecisionRequest request)
    {
        var startTime = DateTime.UtcNow;
        var result = new DecisionResult
        {
            Code = request.Code
        };

        try
        {
            // Step 1: 分析
            var analyzeResult = await ExecuteAnalysisAsync(request.Code);
            if (!analyzeResult.Success)
            {
                result.Success = false;
                result.Message = $"Analysis failed: {analyzeResult.Message}";
                return result;
            }
            result.Analysis = analyzeResult.Output;

            // Step 2: 信号生成
            var signalResult = await ExecuteSignalGenerationAsync(request.Code);
            if (!signalResult.Success)
            {
                result.Success = false;
                result.Message = $"Signal generation failed: {signalResult.Message}";
                return result;
            }
            result.Signals = signalResult.Output;

            // Step 3: 风控检查（对每个信号分别检查）
            var signals = ExtractSignals(signalResult);
            var riskResults = new List<object>();
            var orders = new List<OrderResult>();

            foreach (var signal in signals)
            {
                var riskResult = await ExecuteRiskCheckAsync(signal, request.TotalCapital);
                if (riskResult != null)
                {
                    riskResults.Add(riskResult.Output);

                    // Step 4: 风控通过后自动下单
                    if (riskResult.Success && IsRiskPassed(riskResult))
                    {
                        var orderResult = await PlaceOrderAsync(signal, request.TotalCapital);
                        orders.Add(orderResult);
                        _logger.LogInformation(
                            "Auto order placed for {Code}: {Side} {Volume}@{Price}, OrderId={OrderId}",
                            signal.Code, signal.SignalType, signal.Volume > 0 ? signal.Volume : (long)(request.TotalCapital * 0.1m / signal.Price / 100) * 100, signal.Price, orderResult.OrderId);
                    }
                }
            }

            result.Success = true;
            result.RiskCheck = riskResults.Count > 0
                ? new Dictionary<string, object> { ["checks"] = riskResults }
                : null;
            result.Orders = orders;
            result.Message = signals.Count > 0
                ? $"Decision completed: {signals.Count} signals, {orders.Count} orders placed"
                : "Decision completed: no actionable signals";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autonomous decision failed for {Code}", request.Code);
            result.Success = false;
            result.Message = ex.Message;
        }

        result.ExecutionTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        return result;
    }

    /// <summary>
    /// 批量决策（并行执行）
    /// </summary>
    public async Task<List<DecisionResult>> MakeBatchDecisionAsync(List<string> codes, decimal totalCapital)
    {
        var tasks = codes.Select(code =>
        {
            var request = new DecisionRequest
            {
                Code = code,
                TotalCapital = totalCapital
            };
            return MakeDecisionAsync(request);
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<AgentResult> ExecuteAnalysisAsync(string code)
    {
        var agent = _orchestrator.GetAgent("research-agent");
        if (agent == null)
        {
            return new AgentResult { Success = false, Message = "Research agent not found" };
        }

        var task = new AgentTask
        {
            TaskType = "analyze-stock",
            Parameters = new Dictionary<string, object> { ["code"] = code }
        };

        return await agent.ExecuteAsync(task);
    }

    private async Task<AgentResult> ExecuteSignalGenerationAsync(string code)
    {
        var agent = _orchestrator.GetAgent("alpha-agent");
        if (agent == null)
        {
            return new AgentResult { Success = false, Message = "Alpha agent not found" };
        }

        var task = new AgentTask
        {
            TaskType = "generate-signal",
            Parameters = new Dictionary<string, object> { ["code"] = code }
        };

        return await agent.ExecuteAsync(task);
    }

    private async Task<AgentResult?> ExecuteRiskCheckAsync(TradeSignal signal, decimal totalCapital)
    {
        var agent = _orchestrator.GetAgent("risk-agent");
        if (agent == null)
        {
            return null;
        }

        var task = new AgentTask
        {
            TaskType = "check-risk",
            Parameters = new Dictionary<string, object>
            {
                ["signal"] = signal,
                ["totalCapital"] = totalCapital
            }
        };

        return await agent.ExecuteAsync(task);
    }

    private async Task<OrderResult> PlaceOrderAsync(TradeSignal signal, decimal totalCapital)
    {
        // 计算下单量：默认单票仓位不超过总资金的10%
        var maxPositionValue = totalCapital * 0.1m;
        var volume = signal.Volume > 0
            ? signal.Volume
            : (long)(maxPositionValue / signal.Price / 100) * 100; // 按手取整

        if (volume <= 0)
        {
            _logger.LogWarning("Calculated volume is 0 for {Code} at price {Price}, skipping order", signal.Code, signal.Price);
            return new OrderResult
            {
                Success = false,
                Message = "Volume is 0, order skipped"
            };
        }

        var orderRequest = new OrderRequest
        {
            Code = signal.Code,
            Side = signal.SignalType.ToString().ToLower(),
            OrderType = Core.Enums.OrderType.Limit,
            Price = signal.Price,
            Volume = volume,
            StrategyName = signal.StrategyName,
            SignalId = signal.SignalId
        };

        return await _orderManager.PlaceOrderAsync(orderRequest);
    }

    private static List<TradeSignal> ExtractSignals(AgentResult signalResult)
    {
        var signals = new List<TradeSignal>();

        if (signalResult.Output.TryGetValue("signals", out var signalsObj))
        {
            if (signalsObj is List<TradeSignal> signalList)
                signals = signalList;
            else if (signalsObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                signals = System.Text.Json.JsonSerializer.Deserialize<List<TradeSignal>>(
                    jsonElement.GetRawText()) ?? new List<TradeSignal>();
            }
        }

        return signals;
    }

    private static bool IsRiskPassed(AgentResult riskResult)
    {
        if (riskResult.Output.TryGetValue("riskCheckResult", out var riskObj))
        {
            // RiskCheckResult has Passed property
            if (riskObj is RiskCheckResult checkResult)
                return checkResult.Passed;

            if (riskObj is System.Text.Json.JsonElement jsonElement &&
                jsonElement.TryGetProperty("passed", out var passedProp))
                return passedProp.GetBoolean();
        }

        return false;
    }
}

/// <summary>
/// 决策请求
/// </summary>
public class DecisionRequest
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 总资金
    /// </summary>
    public decimal TotalCapital { get; set; }
}

/// <summary>
/// 决策结果
/// </summary>
public class DecisionResult
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 分析结果
    /// </summary>
    public Dictionary<string, object> Analysis { get; set; } = new();

    /// <summary>
    /// 信号结果
    /// </summary>
    public Dictionary<string, object> Signals { get; set; } = new();

    /// <summary>
    /// 风控结果
    /// </summary>
    public Dictionary<string, object>? RiskCheck { get; set; }

    /// <summary>
    /// 订单结果列表
    /// </summary>
    public List<OrderResult> Orders { get; set; } = new();

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 执行时间
    /// </summary>
    public long ExecutionTime { get; set; }
}
