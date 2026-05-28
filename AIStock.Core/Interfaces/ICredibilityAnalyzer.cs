using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 真假识别接口
/// </summary>
public interface ICredibilityAnalyzer
{
    /// <summary>
    /// 分析事件可信度
    /// </summary>
    Task<CredibilityResult> AnalyzeAsync(EventData eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查历史重复
    /// </summary>
    Task<List<string>> CheckHistoricalDuplicatesAsync(string title, string content, CancellationToken cancellationToken = default);
}

/// <summary>
/// 传播链分析接口
/// </summary>
public interface ISpreadAnalyzer
{
    /// <summary>
    /// 分析传播链
    /// </summary>
    Task<SpreadAnalysisResult> AnalyzeSpreadAsync(string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算热度斜率
    /// </summary>
    Task<double> CalculateHeatSlopeAsync(string keyword, int hours = 24, CancellationToken cancellationToken = default);
}
