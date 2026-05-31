namespace AIStock.Core.Models;

/// <summary>
/// 实时行情数据（不可变）
/// </summary>
public record QuoteData
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 当前价格（元）
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// 昨收价（元）
    /// </summary>
    public decimal PreClose { get; init; }

    /// <summary>
    /// 开盘价（元）
    /// </summary>
    public decimal Open { get; init; }

    /// <summary>
    /// 最高价（元）
    /// </summary>
    public decimal High { get; init; }

    /// <summary>
    /// 最低价（元）
    /// </summary>
    public decimal Low { get; init; }

    /// <summary>
    /// 成交量（股）
    /// </summary>
    public long Volume { get; init; }

    /// <summary>
    /// 成交额（元）
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// 涨跌幅（%）
    /// </summary>
    public decimal ChangePercent { get; init; }

    /// <summary>
    /// 涨跌额（元）
    /// </summary>
    public decimal ChangeAmount { get; init; }

    /// <summary>
    /// 换手率（%）
    /// </summary>
    public decimal TurnoverRate { get; init; }

    /// <summary>
    /// 量比
    /// </summary>
    public decimal VolumeRatio { get; init; }

    /// <summary>
    /// 委比
    /// </summary>
    public decimal WeiBi { get; init; }

    /// <summary>
    /// 内盘
    /// </summary>
    public long InnerVolume { get; init; }

    /// <summary>
    /// 外盘
    /// </summary>
    public long OuterVolume { get; init; }

    /// <summary>
    /// 总市值（元）
    /// </summary>
    public decimal TotalMarketCap { get; init; }

    /// <summary>
    /// 流通市值（元）
    /// </summary>
    public decimal FloatMarketCap { get; init; }

    /// <summary>
    /// 市盈率（TTM）
    /// </summary>
    public decimal PeTtm { get; init; }

    /// <summary>
    /// 市净率
    /// </summary>
    public decimal Pb { get; init; }

    /// <summary>
    /// 数据时间
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; init; } = string.Empty;
}
