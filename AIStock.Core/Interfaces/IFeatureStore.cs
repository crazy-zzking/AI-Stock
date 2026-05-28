using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// Feature Store接口
/// </summary>
public interface IFeatureStore
{
    /// <summary>
    /// 保存特征
    /// </summary>
    Task SaveFeaturesAsync(string code, DateTime dateTime, TechnicalIndicator indicators);

    /// <summary>
    /// 获取最新特征
    /// </summary>
    Task<TechnicalIndicator?> GetLatestFeaturesAsync(string code);

    /// <summary>
    /// 获取历史特征
    /// </summary>
    Task<List<TechnicalIndicator>> GetHistoricalFeaturesAsync(string code, DateTime startTime, DateTime endTime);

    /// <summary>
    /// 批量保存特征
    /// </summary>
    Task BatchSaveFeaturesAsync(List<(string Code, DateTime DateTime, TechnicalIndicator Indicators)> features);

    /// <summary>
    /// 清理过期特征
    /// </summary>
    Task CleanupExpiredFeaturesAsync(TimeSpan maxAge);
}
