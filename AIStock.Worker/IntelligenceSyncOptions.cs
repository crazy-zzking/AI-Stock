namespace AIStock.Worker;

/// <summary>
/// 情报采集配置
/// </summary>
public class IntelligenceSyncOptions
{
    public const string SectionName = "IntelligenceSync";

    /// <summary>是否启用情报采集</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动后延迟立即跑一次</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>采集周期（分钟）</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>是否采集新闻/公告（HttpClient，轻量）</summary>
    public bool CollectNews { get; set; } = true;

    /// <summary>每轮采集新闻条数</summary>
    public int NewsCount { get; set; } = 20;

    /// <summary>是否采集研报（Playwright 无头浏览器）</summary>
    public bool CollectReports { get; set; } = true;

    /// <summary>每轮采集研报条数</summary>
    public int ReportCount { get; set; } = 10;

    /// <summary>每条情报 LLM 抽取之间的节流间隔（毫秒），控制 LLM 调用频率</summary>
    public int ItemThrottleMs { get; set; } = 500;
}
