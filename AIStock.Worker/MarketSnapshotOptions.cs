namespace AIStock.Worker;

/// <summary>
/// 市场快照 / 龙虎榜采集参数
/// </summary>
public class MarketSnapshotOptions
{
    public const string SectionName = "MarketSnapshot";

    /// <summary>每只股票之间的限流间隔（毫秒），防止打爆数据源</summary>
    public int ItemThrottleMs { get; set; } = 50;

    /// <summary>单次采集最大股票数（0 = 不限）。用于联调阶段限量</summary>
    public int MaxStocks { get; set; } = 0;

    /// <summary>是否启用盘中采集（交易时段周期用腾讯批量刷新快照，支持盘中选股）</summary>
    public bool EnableIntraday { get; set; } = true;

    /// <summary>盘中采集间隔（分钟）</summary>
    public int IntradayIntervalMinutes { get; set; } = 20;

    /// <summary>盘中腾讯批量报价每批股票数</summary>
    public int IntradayQuoteBatch { get; set; } = 60;

    /// <summary>尾盘决策时点(HH:mm)。盘中刷新会对齐到此时点跑最后一次，供尾盘选股/下单用最新快照。空则不对齐。</summary>
    public string CloseDecisionTime { get; set; } = "14:55";

    /// <summary>尾盘选股(selection-daily)执行前是否先刷一次盘中快照，保证候选数据=选股时点数据。需 EnableIntraday=true。</summary>
    public bool RefreshBeforeTailSelection { get; set; } = true;

    /// <summary>龙虎榜数据接口地址（东财 datacenter，字段以实测为准）</summary>
    public string DragonTigerUrl { get; set; } =
        "https://datacenter-web.eastmoney.com/api/data/v1/get";
}
