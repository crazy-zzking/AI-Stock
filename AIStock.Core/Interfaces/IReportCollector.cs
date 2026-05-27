using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 研报采集接口
/// </summary>
public interface IReportCollector
{
    /// <summary>
    /// 采集器ID
    /// </summary>
    string CollectorId { get; }

    /// <summary>
    /// 采集研报列表
    /// </summary>
    Task<List<ReportData>> CollectReportsAsync(int count = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// 采集指定股票的研报
    /// </summary>
    Task<List<ReportData>> CollectReportsByStockAsync(string stockCode, int count = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// 采集研报详情
    /// </summary>
    Task<ReportData?> GetReportDetailAsync(string url, CancellationToken cancellationToken = default);
}
