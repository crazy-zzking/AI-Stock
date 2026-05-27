using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 事件抽取引擎接口
/// </summary>
public interface IEventExtractor
{
    /// <summary>
    /// 从文本中抽取事件
    /// </summary>
    Task<EventData> ExtractEventAsync(string text, string eventType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从研报中抽取事件
    /// </summary>
    Task<EventData> ExtractFromReportAsync(ReportData report, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从新闻中抽取事件
    /// </summary>
    Task<EventData> ExtractFromNewsAsync(NewsData news, CancellationToken cancellationToken = default);
}
