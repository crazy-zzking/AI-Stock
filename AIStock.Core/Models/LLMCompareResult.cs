namespace AIStock.Core.Models;

/// <summary>
/// 多模型对比结果
/// </summary>
public class LLMCompareResult
{
    /// <summary>
    /// 是否成功（至少一个模型成功）
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 各模型响应
    /// </summary>
    public List<LLMResponse> Responses { get; set; } = new();

    /// <summary>
    /// 投票结果（多数模型一致的内容）
    /// </summary>
    public string? VotedContent { get; set; }

    /// <summary>
    /// 投票一致率
    /// </summary>
    public double ConsensusRate { get; set; }

    /// <summary>
    /// 最佳响应（根据一致性选择）
    /// </summary>
    public LLMResponse? BestResponse { get; set; }

    /// <summary>
    /// 总耗时（毫秒）
    /// </summary>
    public long TotalTimeMs { get; set; }
}
