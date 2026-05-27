namespace AIStock.Core.Models;

/// <summary>
/// 分笔成交数据
/// </summary>
public class TradeData
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 时间
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// 价格（元）
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 成交量（股）
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 成交单数
    /// </summary>
    public int OrderCount { get; set; }

    /// <summary>
    /// 方向（1=买，-1=卖，0=中性）
    /// </summary>
    public int Direction { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 集合竞价数据
/// </summary>
public class CallAuctionData
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 时间
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// 价格（元）
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 匹配成交量
    /// </summary>
    public long MatchVolume { get; set; }

    /// <summary>
    /// 未匹配量
    /// </summary>
    public long UnmatchedVolume { get; set; }

    /// <summary>
    /// 标记（1=未匹配买量，-1=未匹配卖量）
    /// </summary>
    public int Flag { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 股票池/代码信息
/// </summary>
public class StockCodeInfo
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 市场
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 证券类型（Stock/Index/ETF）
    /// </summary>
    public string SecurityType { get; set; } = "Stock";

    /// <summary>
    /// 小数位数
    /// </summary>
    public int DecimalPlaces { get; set; }

    /// <summary>
    /// 昨收价（元）
    /// </summary>
    public decimal LastPrice { get; set; }
}

/// <summary>
/// 证券类型枚举
/// </summary>
public enum SecurityType
{
    /// <summary>
    /// 股票
    /// </summary>
    Stock,

    /// <summary>
    /// 指数
    /// </summary>
    Index,

    /// <summary>
    /// ETF
    /// </summary>
    ETF
}
