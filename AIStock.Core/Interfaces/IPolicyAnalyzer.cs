using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 政策分析接口
/// </summary>
public interface IPolicyAnalyzer
{
    /// <summary>
    /// 分析政策
    /// </summary>
    Task<ReportAnalysis> AnalyzeAsync(string policyTitle, string policyContent, CancellationToken cancellationToken = default);
}
