using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 新闻采集接口
/// </summary>
public interface INewsCollector
{
    /// <summary>
    /// 采集器ID
    /// </summary>
    string CollectorId { get; }

    /// <summary>
    /// 采集最新新闻
    /// </summary>
    Task<List<NewsData>> CollectLatestNewsAsync(int count = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// 采集指定股票的新闻
    /// </summary>
    Task<List<NewsData>> CollectNewsByStockAsync(string stockCode, int count = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// 采集指定类别的新闻
    /// </summary>
    Task<List<NewsData>> CollectNewsByCategoryAsync(string category, int count = 50, CancellationToken cancellationToken = default);
}
