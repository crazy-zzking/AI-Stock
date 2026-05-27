namespace AIStock.Core.Models;

/// <summary>
/// 资金流向数据
/// </summary>
public class CapitalFlowData
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 日期
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 主力净流入（元）
    /// </summary>
    public decimal MainNetInflow { get; set; }

    /// <summary>
    /// 超大单净流入（元）
    /// </summary>
    public decimal SuperLargeNetInflow { get; set; }

    /// <summary>
    /// 大单净流入（元）
    /// </summary>
    public decimal LargeNetInflow { get; set; }

    /// <summary>
    /// 中单净流入（元）
    /// </summary>
    public decimal MediumNetInflow { get; set; }

    /// <summary>
    /// 小单净流入（元）
    /// </summary>
    public decimal SmallNetInflow { get; set; }

    /// <summary>
    /// 主力净流入占比（%）
    /// </summary>
    public decimal MainNetInflowPercent { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
