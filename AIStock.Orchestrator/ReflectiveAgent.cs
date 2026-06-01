using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Orchestrator;

/// <summary>
/// 反射式Agent装饰器 — 包装IAgent，执行后通过LLM自我反思、纠错
/// </summary>
public class ReflectiveAgent : IAgent
{
    private readonly IAgent _inner;
    private readonly ILLMService _llmService;
    private readonly ILogger<ReflectiveAgent> _logger;
    private readonly int _maxReflections;

    public ReflectiveAgent(
        IAgent inner,
        ILLMService llmService,
        ILogger<ReflectiveAgent> logger,
        int maxReflections = 2)
    {
        _inner = inner;
        _llmService = llmService;
        _logger = logger;
        _maxReflections = maxReflections;
    }

    public string AgentId => _inner.AgentId;
    public string Name => _inner.Name;
    public string Type => _inner.Type;

    public async Task<AgentResult> ExecuteAsync(AgentTask task)
    {
        var startTime = DateTime.Now;

        // Step 1: 初始执行
        var result = await _inner.ExecuteAsync(task);
        if (!result.Success)
        {
            _logger.LogDebug("Agent {AgentId} initial execution failed, skipping reflection", AgentId);
            return result;
        }

        // Step 2: 多轮反思
        for (int i = 0; i < _maxReflections; i++)
        {
            try
            {
                var reflectionResult = await ReflectAsync(task, result, i + 1);
                if (reflectionResult == null)
                {
                    _logger.LogDebug("Agent {AgentId} reflection round {Round}: no issues found", AgentId, i + 1);
                    break; // 无问题，结束反思
                }

                _logger.LogInformation("Agent {AgentId} reflection round {Round}: issues found, retrying...",
                    AgentId, i + 1);
                result = reflectionResult;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent {AgentId} reflection round {Round} failed", AgentId, i + 1);
                break;
            }
        }

        result.ExecutionTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
        return result;
    }

    public Task<AgentStatus> GetStatusAsync() => _inner.GetStatusAsync();

    private async Task<AgentResult?> ReflectAsync(AgentTask task, AgentResult currentResult, int round)
    {
        // 构建反思 Prompt
        var reflectionPrompt = BuildReflectionPrompt(task, currentResult, round);

        var llmResponse = await _llmService.SendAsync(new LLMRequest
        {
            SystemPrompt = "你是一位严谨的量化分析审计师。请检查以下Agent分析结果是否存在逻辑矛盾、数据遗漏或错误推理。仅输出JSON格式：{\"hasIssues\":true/false,\"issues\":[\"...\"],\"corrections\":{\"key\":\"newValue\"}}",
            UserPrompt = reflectionPrompt,
            Temperature = 0.1m
        });

        if (!llmResponse.Success || string.IsNullOrEmpty(llmResponse.Content))
            return null;

        // 解析LLM反馈
        var hasIssues = llmResponse.Content.Contains("\"hasIssues\":true", StringComparison.OrdinalIgnoreCase) ||
                        llmResponse.Content.Contains("\"hasIssues\": true", StringComparison.OrdinalIgnoreCase);

        if (!hasIssues)
            return null;

        // 有反思建议：提取修正并重新执行
        _logger.LogInformation("Agent {AgentId} reflection found: {Content}", AgentId,
            llmResponse.Content[..Math.Min(200, llmResponse.Content.Length)]);

        // 将反思结果注入任务参数，重新执行
        var correctedTask = new AgentTask
        {
            TaskType = task.TaskType,
            Parameters = new Dictionary<string, object>(task.Parameters)
            {
                ["reflection_round"] = round,
                ["reflection_feedback"] = llmResponse.Content,
                ["previous_result"] = currentResult.Output
            }
        };

        return await _inner.ExecuteAsync(correctedTask);
    }

    private static string BuildReflectionPrompt(AgentTask task, AgentResult result, int round)
    {
        var outputSummary = System.Text.Json.JsonSerializer.Serialize(result.Output,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        return $"""
        任务类型：{task.TaskType}
        任务参数：{System.Text.Json.JsonSerializer.Serialize(task.Parameters)}
        分析结果：
        {outputSummary}
        消息：{result.Message}
        执行耗时：{result.ExecutionTime}ms

        请检查上述分析结果是否有以下问题：
        1. 数据矛盾（如价格与技术指标方向矛盾）
        2. 数值异常（超出合理范围）
        3. 逻辑推理错误
        4. 关键信息遗漏

        这是第{round}轮反思检查。
        """;
    }
}
