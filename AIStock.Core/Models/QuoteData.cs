namespace AIStock.Core.Models;

/// <summary>
/// 实时行情数据
/// </summary>
public class QuoteData
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
    /// 当前价格（元）
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 昨收价（元）
    /// </summary>
    public decimal PreClose { get; set; }

    /// <summary>
    /// 开盘价（元）
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// 最高价（元）
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// 最低价（元）
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// 成交量（股）
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 成交额（元）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 涨跌幅（%）
    /// </summary>
    public decimal ChangePercent { get; set; }

    /// <summary>
    /// 涨跌额（元）
    /// </summary>
    public decimal ChangeAmount { get; set; }

    /// <summary>
    /// 换手率（%）
    /// </summary>
    public decimal TurnoverRate { get; set; }

    /// <summary>
    /// 量比
    /// </summary>
    public decimal VolumeRatio { get; set; }

    /// <summary>
    /// 委比
    /// </summary>
    public decimal WeiBi { get; set; }

    /// <summary>
    /// 内盘
    /// </summary>
    public long InnerVolume { get; set; }

    /// <summary>
    /// 外盘
    /// </summary>
    public long OuterVolume { get; set; }

    /// <summary>
    /// 数据时间
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
