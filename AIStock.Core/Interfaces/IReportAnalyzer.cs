using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 研报分析接口
/// </summary>
public interface IReportAnalyzer
{
    /// <summary>
    /// 分析研报
    /// </summary>
    Task<ReportAnalysis> AnalyzeAsync(ReportData report, CancellationToken cancellationToken = default);
}
