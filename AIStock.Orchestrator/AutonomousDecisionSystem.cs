using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator;

/// <summary>
/// 自主决策系统 — 自动分析市场并生成交易决策，风控通过后：
///   - Buy/StrongBuy → 入交易候选池（不入自动下单，需人工确认）
///   - Sell/StrongSell → 自动下单卖出
/// </summary>
public class AutonomousDecisionSystem
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IOrderManager _orderManager;
    private readonly IPositionSizer _positionSizer;
    private readonly AIStockDbContext _db;
    private readonly ILogger<AutonomousDecisionSystem> _logger;

    public AutonomousDecisionSystem(
        IAgentOrchestrator orchestrator,
        IDataProviderResolver dataProviderResolver,
        IOrderManager orderManager,
        IPositionSizer positionSizer,
        AIStockDbContext db,
        ILogger<AutonomousDecisionSystem> logger)
    {
        _orchestrator = orchestrator;
        _dataProviderResolver = dataProviderResolver;
        _orderManager = orderManager;
        _positionSizer = positionSizer;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 执行自主决策流程
    /// </summary>
    public async Task<DecisionResult> MakeDecisionAsync(DecisionRequest request)
    {
        var startTime = DateTime.Now;
        var result = new DecisionResult
        {
            Code = request.Code
        };

        _logger.LogInformation("Decision started for {Code} (capital={Capital})", request.Code, request.TotalCapital);

        try
        {
            // Step 1: 分析
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var analyzeResult = await ExecuteAnalysisAsync(request.Code);
            sw.Stop();
            _logger.LogInformation("Step1/Analysis {Code} {Status} in {ElapsedMs}ms",
                request.Code, analyzeResult.Success ? "OK" : "FAIL", sw.ElapsedMilliseconds);

            if (!analyzeResult.Success)
            {
                result.Success = false;
                result.Message = $"Analysis failed: {analyzeResult.Message}";
                return result;
            }
            result.Analysis = analyzeResult.Output;

            // Step 2: 信号生成
            sw.Restart();
            var signalResult = await ExecuteSignalGenerationAsync(request.Code);
            sw.Stop();
            _logger.LogInformation("Step2/Signal {Code} {Status} in {ElapsedMs}ms",
                request.Code, signalResult.Success ? "OK" : "FAIL", sw.ElapsedMilliseconds);

            if (!signalResult.Success)
            {
                result.Success = false;
                result.Message = $"Signal generation failed: {signalResult.Message}";
                return result;
            }
            result.Signals = signalResult.Output;

            // Step 3: 风控检查（对每个信号分别检查）
            var signals = ExtractSignals(signalResult);
            _logger.LogInformation("Step3/Risk {Code} — {SignalCount} signals to check", request.Code, signals.Count);

            var riskResults = new List<object>();
            var orders = new List<OrderResult>();
            var candidatesAdded = 0;

            foreach (var signal in signals)
            {
                sw.Restart();
                var riskResult = await ExecuteRiskCheckAsync(signal, request.TotalCapital);
                sw.Stop();

                if (riskResult != null)
                {
                    var passed = IsRiskPassed(riskResult);
                    _logger.LogInformation(
                        "Step3/Risk {Code} signal={SignalType} strength={Strength} passed={Passed} in {ElapsedMs}ms",
                        signal.Code, signal.SignalType, signal.Strength, passed, sw.ElapsedMilliseconds);

                    riskResults.Add(riskResult.Output);

                    // Step 4: 风控通过后 买入→入候选池 / 卖出→自动下单
                    if (riskResult.Success && passed)
                    {
                        sw.Restart();
                        var side = ResolveSide(signal.SignalType);
                        if (side == "buy")
                        {
                            // 买入信号 → 入交易候选池，不自动下单
                            var added = await AddToCandidatePoolAsync(signal, request.TotalCapital, request.PositionSizeMode);
                            candidatesAdded += added;
                            orders.Add(new OrderResult
                            {
                                Success = added > 0,
                                Message = added > 0
                                    ? $"已加入候选池 (orderId=candidate-{signal.Code})"
                                    : "入候选池失败",
                            });
                            _logger.LogInformation(
                                "Step4/CandidatePool {Code} signal={SignalType} price={Price} added={Added} in {ElapsedMs}ms",
                                signal.Code, signal.SignalType, signal.Price, added, sw.ElapsedMilliseconds);
                        }
                        else
                        {
                            // 卖出信号 → 自动下单
                            var orderResult = await PlaceOrderAsync(signal, request.TotalCapital, request.PositionSizeMode);
                            orders.Add(orderResult);
                            _logger.LogInformation(
                                "Step4/Order {Code} side={Side} price={Price} volume={Volume} orderId={OrderId} success={Success} in {ElapsedMs}ms",
                                signal.Code, signal.SignalType, signal.Price, signal.Volume,
                                orderResult.OrderId, orderResult.Success, sw.ElapsedMilliseconds);
                        }
                        sw.Stop();
                    }
                    else if (riskResult.Success && !passed)
                    {
                        _logger.LogWarning("Step4/Order {Code} skipped — risk check not passed", signal.Code);
                    }
                }
            }

            result.Success = true;
            result.RiskCheck = riskResults.Count > 0
                ? new Dictionary<string, object> { ["checks"] = riskResults }
                : null;
            result.Orders = orders;
            result.CandidatesAdded = candidatesAdded;
            result.Message = signals.Count > 0
                ? $"Decision completed: {signals.Count} signals, {orders.Count} orders, {candidatesAdded} candidates added to pool"
                : "Decision completed: no actionable signals";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autonomous decision failed for {Code}", request.Code);
            result.Success = false;
            result.Message = ex.Message;
        }

        result.ExecutionTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
        _logger.LogInformation(
            "Decision finished {Code} success={Success} orders={OrderCount} candidates={CandidateCount} totalMs={ElapsedMs}",
            request.Code, result.Success, result.Orders.Count, result.CandidatesAdded, result.ExecutionTime);
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

    /// <summary>
    /// 买入信号 → 入交易候选池（upsert）。
    /// 已处理(status!=0)的候选不覆盖；返回 1=新增 1=刷新 0=跳过。
    /// </summary>
    private async Task<int> AddToCandidatePoolAsync(TradeSignal signal, decimal totalCapital, PositionSizeMode mode)
    {
        try
        {
            if (signal.Price <= 0)
            {
                _logger.LogWarning("Candidate pool skip {Code}: price <= 0", signal.Code);
                return 0;
            }

            var today = DateTime.Today;

            // 仓位量计算
            long volume;
            if (signal.Volume > 0)
                volume = signal.Volume;
            else
            {
                var positionValue = _positionSizer.CalculatePositionSize(signal, totalCapital, mode);
                volume = (long)(positionValue / signal.Price / 100) * 100;
            }
            if (volume < 100) volume = 100;

            // 加载 K 线用于价位计算
            var since = today.AddDays(-30);
            var bars = await _db.KlineData
                .Where(k => k.Interval == nameof(KlineInterval.Daily) && k.Code == signal.Code
                            && k.DateTime >= since && k.DateTime <= today)
                .OrderBy(k => k.DateTime)
                .Select(k => new PriceLevelCalculator.PriceBar(k.High, k.Low, k.Close))
                .ToListAsync();

            var plan = PriceLevelCalculator.Compute(bars, signal.Price);

            // 查找当日已有候选
            var existing = await _db.TradeCandidate
                .FirstOrDefaultAsync(c => c.TradingDate == today && c.Code == signal.Code);

            if (existing != null)
            {
                if (existing.Status != 0) return 0; // 用户已处理，不覆盖
                existing.Score = signal.Strength;
                existing.Narrative = signal.Reason ?? string.Empty;
                existing.RefClose = signal.Price;
                existing.BuyLow = plan.BuyLow;
                existing.BuyHigh = plan.BuyHigh;
                existing.StopLoss = plan.StopLoss;
                existing.TakeProfit = plan.TakeProfit;
                existing.PlanBasis = plan.Basis;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _db.TradeCandidate.Add(new TradeCandidateEntity
                {
                    TradingDate = today,
                    Code = signal.Code,
                    Name = signal.Code, // 后续由其他服务补名称
                    Score = signal.Strength,
                    TopStrategy = signal.StrategyName ?? "autonomous",
                    TopStrategyName = signal.StrategyName ?? "自主决策",
                    HitStrategies = JsonSerializer.Serialize(
                        new[] { signal.StrategyName ?? "autonomous" }, Core.Json.AppJson.Default),
                    HitCount = 1,
                    Narrative = signal.Reason ?? string.Empty,
                    RefClose = signal.Price,
                    BuyLow = plan.BuyLow,
                    BuyHigh = plan.BuyHigh,
                    StopLoss = plan.StopLoss,
                    TakeProfit = plan.TakeProfit,
                    PlanBasis = plan.Basis,
                    Status = 0,
                });
            }

            await _db.SaveChangesAsync();
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add {Code} to candidate pool", signal.Code);
            return 0;
        }
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

    /// <summary>
    /// 卖出信号 → 自动下单（保留实盘卖出能力）。
    /// </summary>
    private async Task<OrderResult> PlaceOrderAsync(TradeSignal signal, decimal totalCapital, PositionSizeMode mode)
    {
        // 下单量：优先使用信号显式数量，否则由 PositionSizer 按所选模式（Kelly/波动率目标等）计算仓位金额
        long volume;
        if (signal.Volume > 0)
        {
            volume = signal.Volume;
        }
        else
        {
            if (signal.Price <= 0)
            {
                _logger.LogWarning("Signal price <= 0 for {Code}, skipping order", signal.Code);
                return new OrderResult { Success = false, Message = "Invalid signal price, order skipped" };
            }

            var positionValue = _positionSizer.CalculatePositionSize(signal, totalCapital, mode);
            volume = (long)(positionValue / signal.Price / 100) * 100; // 按手(100股)向下取整
        }

        if (volume <= 0)
        {
            _logger.LogWarning("Calculated volume is 0 for {Code} at price {Price} (mode {Mode}), skipping order",
                signal.Code, signal.Price, mode);
            return new OrderResult
            {
                Success = false,
                Message = "Volume is 0, order skipped"
            };
        }

        var side = ResolveSide(signal.SignalType);
        if (side == null)
        {
            _logger.LogInformation("Signal type {Type} for {Code} is not actionable, skipping order", signal.SignalType, signal.Code);
            return new OrderResult { Success = false, Message = $"Signal {signal.SignalType} not actionable, order skipped" };
        }

        var orderRequest = new OrderRequest
        {
            Code = signal.Code,
            Side = side,
            OrderType = Core.Enums.OrderType.Limit,
            Price = signal.Price,
            Volume = volume,
            StrategyName = signal.StrategyName,
            SignalId = signal.SignalId
        };

        return await _orderManager.PlaceOrderAsync(orderRequest);
    }

    /// <summary>
    /// 将信号类型映射为下单方向。Buy/StrongBuy → buy，Sell/StrongSell → sell，Hold/未知 → null（不下单）。
    /// </summary>
    private static string? ResolveSide(SignalType signalType) => signalType switch
    {
        SignalType.Buy or SignalType.StrongBuy => "buy",
        SignalType.Sell or SignalType.StrongSell => "sell",
        _ => null
    };

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

    /// <summary>
    /// 仓位计算模式，默认半 Kelly
    /// </summary>
    public PositionSizeMode PositionSizeMode { get; set; } = PositionSizeMode.Kelly;
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
    /// 订单结果列表（仅卖出订单；买入已入候选池）
    /// </summary>
    public List<OrderResult> Orders { get; set; } = new();

    /// <summary>
    /// 入候选池条数
    /// </summary>
    public int CandidatesAdded { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 执行时间
    /// </summary>
    public long ExecutionTime { get; set; }
}
