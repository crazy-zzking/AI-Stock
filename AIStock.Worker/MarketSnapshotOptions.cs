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

    /// <summary>龙虎榜数据接口地址（东财 datacenter，字段以实测为准）</summary>
    public string DragonTigerUrl { get; set; } =
        "https://datacenter-web.eastmoney.com/api/data/v1/get";
}
