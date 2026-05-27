using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// LLM模型提供者接口
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// 提供者ID
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// 发送请求
    /// </summary>
    Task<LLMResponse> SendAsync(LLMConfig config, LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式发送请求
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(LLMConfig config, LLMRequest request, CancellationToken cancellationToken = default);
}
