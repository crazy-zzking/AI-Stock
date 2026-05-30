namespace AIStock.Worker;

/// <summary>
/// 数据同步配置
/// </summary>
public class DataSyncOptions
{
    public const string SectionName = "DataSync";

    /// <summary>是否启用数据同步</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动时立即跑一次</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>同步周期（小时）</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>是否同步K线</summary>
    public bool SyncKlines { get; set; } = true;

    /// <summary>每只股票同步的日K条数</summary>
    public int KlineCount { get; set; } = 120;

    /// <summary>本轮最多同步多少只股票的K线（0 表示全部）</summary>
    public int MaxStocks { get; set; } = 300;

    /// <summary>每只股票K线请求之间的节流间隔（毫秒），避免压垮数据源</summary>
    public int KlineThrottleMs { get; set; } = 200;
}
