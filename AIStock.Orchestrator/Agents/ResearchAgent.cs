using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.GraphRAG;
using AIStock.Memory;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator.Agents;

/// <summary>
/// 研究Agent - 负责市场研究和分析
/// </summary>
public class ResearchAgent : IAgent
{
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IGraphRAGService? _graphRAG;
    private readonly ILogger<ResearchAgent> _logger;
    private readonly IAgentMemory? _memory;

    public ResearchAgent(
        IDataProviderResolver dataProviderResolver,
        IFeatureCalculator featureCalculator,
        ILogger<ResearchAgent> logger,
        IAgentMemory? memory = null,
        IGraphRAGService? graphRAG = null)
    {
        _dataProviderResolver = dataProviderResolver;
        _featureCalculator = featureCalculator;
        _logger = logger;
        _memory = memory;
        _graphRAG = graphRAG;
    }

    public string AgentId => "research-agent";
    public string Name => "研究Agent";
    public string Type => "research";

    public async Task<AgentResult> ExecuteAsync(AgentTask task)
    {
        var startTime = DateTime.Now;
        AgentResult result;

        try
        {
            var taskType = task.TaskType.ToLower();
            result = taskType switch
            {
                "analyze-stock" => await AnalyzeStockAsync(task),
                "get-market-overview" => await GetMarketOverviewAsync(task),
                _ => new AgentResult { Success = false, Message = $"Unknown task type: {taskType}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Research agent execution failed");
            result = new AgentResult
            {
                Success = false,
                Message = ex.Message,
                ExecutionTime = (long)(DateTime.Now - startTime).TotalMilliseconds
            };
        }

        // 持久化到Memory
        if (_memory != null)
        {
            _ = _memory.SaveAsync(AgentId, task, result);
        }

        return result;
    }

    public Task<AgentStatus> GetStatusAsync()
    {
        return Task.FromResult(new AgentStatus
        {
            AgentId = AgentId,
            IsOnline = true,
            LastActiveTime = DateTime.Now
        });
    }

    private async Task<AgentResult> AnalyzeStockAsync(AgentTask task)
    {
        var startTime = DateTime.Now;
        var code = task.Parameters.GetValueOrDefault("code")?.ToString() ?? "";

        var provider = _dataProviderResolver.GetDefaultProvider();
        var klines = await provider.GetKlinesAsync(code, Core.Enums.KlineInterval.Daily, 100);

        if (klines == null || klines.Count == 0)
        {
            return new AgentResult
            {
                Success = false,
                Message = $"No data found for {code}"
            };
        }

        var indicators = _featureCalculator.CalculateAll(code, klines);
        var currentPrice = klines.Last().Close;
        var ma = indicators.MA;

        var analysis = new Dictionary<string, object>
        {
            ["code"] = code,
            ["currentPrice"] = currentPrice,
            ["ma5"] = ma?.MA5 ?? 0,
            ["ma10"] = ma?.MA10 ?? 0,
            ["ma20"] = ma?.MA20 ?? 0,
            ["ma60"] = ma?.MA60 ?? 0,
            ["rsi"] = indicators.RSI?.RSI12 ?? 0,
            ["macd"] = indicators.MACD?.MACD ?? 0,
            ["volatility"] = indicators.Volatility ?? 0,
            ["atr"] = indicators.ATR ?? 0
        };

        // 注入图谱上下文（供LLM下游使用）
        if (_graphRAG != null)
        {
            try
            {
                var graphContext = await _graphRAG.BuildContextAsync(code, "analysis");
                analysis["graphContext"] = graphContext.ToPromptText();
                analysis["graphContextRaw"] = graphContext;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build graph context for {Code}", code);
            }
        }

        return new AgentResult
        {
            Success = true,
            Output = analysis,
            Message = $"Analysis completed for {code}",
            ExecutionTime = (long)(DateTime.Now - startTime).TotalMilliseconds
        };
    }

    private async Task<AgentResult> GetMarketOverviewAsync(AgentTask task)
    {
        var startTime = DateTime.Now;

        var provider = _dataProviderResolver.GetDefaultProvider();
        var indexCode = task.Parameters.GetValueOrDefault("indexCode")?.ToString() ?? "000001";
        var klines = await provider.GetKlinesAsync(indexCode, Core.Enums.KlineInterval.Daily, 30);

        if (klines == null || klines.Count == 0)
        {
            return new AgentResult
            {
                Success = false,
                Message = "No market data available"
            };
        }

        var currentPrice = klines.Last().Close;
        var prevPrice = klines[klines.Count - 2].Close;
        var changePercent = (currentPrice - prevPrice) / prevPrice * 100;

        var overview = new Dictionary<string, object>
        {
            ["indexCode"] = indexCode,
            ["currentPrice"] = currentPrice,
            ["changePercent"] = changePercent,
            ["high"] = klines.Last().High,
            ["low"] = klines.Last().Low,
            ["volume"] = klines.Last().Volume
        };

        return new AgentResult
        {
            Success = true,
            Output = overview,
            Message = "Market overview retrieved",
            ExecutionTime = (long)(DateTime.Now - startTime).TotalMilliseconds
        };
    }
}
