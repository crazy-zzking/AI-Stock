using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// LLM服务接口
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// 发送请求到LLM
    /// </summary>
    Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送请求到指定模型
    /// </summary>
    Task<LLMResponse> SendAsync(LLMRequest request, string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 并行调用多个模型并投票
    /// </summary>
    Task<LLMCompareResult> CompareAsync(LLMRequest request, int modelCount = 3, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有可用模型配置
    /// </summary>
    Task<List<LLMConfig>> GetAvailableModelsAsync();

    /// <summary>
    /// 获取指定模型配置
    /// </summary>
    Task<LLMConfig?> GetModelConfigAsync(string modelId);

    /// <summary>
    /// 刷新模型配置缓存
    /// </summary>
    Task RefreshModelConfigsAsync();
}
