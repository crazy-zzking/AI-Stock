using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 强度评分接口
/// </summary>
public interface IIntensityScorer
{
    /// <summary>
    /// 评估事件强度
    /// </summary>
    Task<IntensityScore> ScoreAsync(EventData eventData, CancellationToken cancellationToken = default);
}
