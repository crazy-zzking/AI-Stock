namespace AIStock.Core.Models;

/// <summary>
/// 股票基础信息
/// </summary>
public class StockInfo
{
    /// <summary>
    /// 股票代码（纯数字，如 600519）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 市场（sh/sz/bj）
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 行业
    /// </summary>
    public string Industry { get; set; } = string.Empty;

    /// <summary>
    /// 上市日期
    /// </summary>
    public DateTime? ListDate { get; set; }

    /// <summary>
    /// 是否退市
    /// </summary>
    public bool IsDelisted { get; set; }
}
