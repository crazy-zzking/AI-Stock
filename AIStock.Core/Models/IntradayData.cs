namespace AIStock.Core.Models;

/// <summary>
/// 分时数据
/// </summary>
public class IntradayData
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 时间
    /// </summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>
    /// 价格（元）
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 累计成交量（股）
    /// </summary>
    public long CumulativeVolume { get; set; }

    /// <summary>
    /// 累计成交额（元）
    /// </summary>
    public decimal CumulativeAmount { get; set; }

    /// <summary>
    /// 当分钟成交量（股）
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 均价（元）
    /// </summary>
    public decimal AveragePrice { get; set; }

    /// <summary>
    /// 涨跌幅（%）
    /// </summary>
    public decimal ChangePercent { get; set; }

    /// <summary>
    /// 涨速
    /// </summary>
    public decimal ChangeSpeed { get; set; }

    /// <summary>
    /// 换手率（%）
    /// </summary>
    public decimal TurnoverRate { get; set; }

    /// <summary>
    /// 量比
    /// </summary>
    public decimal VolumeRatio { get; set; }

    /// <summary>
    /// 内盘
    /// </summary>
    public long InnerVolume { get; set; }

    /// <summary>
    /// 外盘
    /// </summary>
    public long OuterVolume { get; set; }

    /// <summary>
    /// 委比
    /// </summary>
    public decimal WeiBi { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
