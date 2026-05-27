namespace AIStock.Core.Models;

/// <summary>
/// 强度评分结果
/// </summary>
public class IntensityScore
{
    /// <summary>
    /// 重磅程度（1-10）
    /// </summary>
    public int Importance { get; set; }

    /// <summary>
    /// 可信度（1-10）
    /// </summary>
    public int Credibility { get; set; }

    /// <summary>
    /// 传播速度评分（1-10）
    /// </summary>
    public int SpreadSpeed { get; set; }

    /// <summary>
    /// 综合评分（1-10）
    /// </summary>
    public int OverallScore { get; set; }

    /// <summary>
    /// 评分理由
    /// </summary>
    public string? Reason { get; set; }
}
