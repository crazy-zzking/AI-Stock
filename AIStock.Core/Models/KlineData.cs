namespace AIStock.Core.Models;

/// <summary>
/// K线数据
/// </summary>
public class KlineData
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 日期时间
    /// </summary>
    public DateTime DateTime { get; set; }

    /// <summary>
    /// 开盘价（元）
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// 收盘价（元）
    /// </summary>
    public decimal Close { get; set; }

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
    /// 换手率（%）
    /// </summary>
    public decimal TurnoverRate { get; set; }

    /// <summary>
    /// 涨跌幅（%）
    /// </summary>
    public decimal ChangePercent { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
