namespace AIStock.Worker;

/// <summary>
/// 数据同步配置
/// </summary>
public class DataSyncOptions
{
    public const string SectionName = "DataSync";

    /// <summary>是否启用数据同步</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>股票池接口地址（返回全市场代码）</summary>
    public string StockCodesUrl { get; set; } = "http://115.29.178.22:8080/api/codes";

    /// <summary>交易日接口地址（range 查询，判断收盘后是否需同步）</summary>
    public string WorkdayUrl { get; set; } = "http://115.29.178.22:8080/api/workday/range";

    /// <summary>收盘同步时刻（小时，24制）。交易日此时刻后同步当日K线</summary>
    public int SyncHour { get; set; } = 16;

    /// <summary>是否同步K线</summary>
    public bool SyncKlines { get; set; } = true;

    /// <summary>每只股票同步的日K条数</summary>
    public int KlineCount { get; set; } = 120;

    /// <summary>本轮最多同步多少只股票的K线（0 表示全部）</summary>
    public int MaxStocks { get; set; } = 300;

    /// <summary>每只股票K线请求之间的节流间隔（毫秒）。仅当 KlineBatchSize&lt;=1（退化为串行）时生效</summary>
    public int KlineThrottleMs { get; set; } = 200;

    /// <summary>K线同步批大小（批内并发拉取）。&gt;1 时分批并行，=1 时退化为串行</summary>
    public int KlineBatchSize { get; set; } = 8;

    /// <summary>K线同步批间延迟（毫秒），避免被东财限流/封 IP</summary>
    public int KlineBatchDelayMs { get; set; } = 100;

    /// <summary>是否同步股票明细（行业 + 概念，来自东财 F10 / 同花顺）</summary>
    public bool SyncDetails { get; set; } = true;

    /// <summary>明细同步本轮最多处理多少只（0 = 全部）</summary>
    public int DetailMaxStocks { get; set; } = 0;

    /// <summary>明细同步批大小（批内并发）</summary>
    public int DetailBatchSize { get; set; } = 10;

    /// <summary>明细同步批间延迟（毫秒）</summary>
    public int DetailBatchDelayMs { get; set; } = 50;
}
