namespace AIStock.Core.Models;

/// <summary>
/// 风控配置参数
/// </summary>
public class RiskConfig
{
    // ========== 仓位限制 ==========
    /// <summary>单票最大仓位比例（%）</summary>
    public decimal MaxSingleStockPercent { get; set; } = 20;
    /// <summary>总仓位最大比例（%）</summary>
    public decimal MaxTotalPositionPercent { get; set; } = 80;
    /// <summary>板块最大集中度（%）</summary>
    public decimal MaxSectorPercent { get; set; } = 40;
    /// <summary>单笔最大交易比例（%）</summary>
    public decimal MaxSingleTradePercent { get; set; } = 10;

    // ========== 风险等级阈值 ==========
    public int CriticalThreshold { get; set; } = 3;
    public int HighThreshold { get; set; } = 2;
    public int MediumThreshold { get; set; } = 1;

    // ========== 止损参数 ==========
    /// <summary>固定止损百分比（%）</summary>
    public decimal DefaultFixedStopPercent { get; set; } = 5;
    /// <summary>移动止损百分比（%）</summary>
    public decimal DefaultTrailingPercent { get; set; } = 8;
    /// <summary>默认止损倍数（相对入场价）</summary>
    public decimal DefaultStopLossMultiplier { get; set; } = 0.95m;
    /// <summary>默认止盈倍数（相对入场价）</summary>
    public decimal DefaultTakeProfitMultiplier { get; set; } = 1.10m;
    /// <summary>移动止盈倍数（相对入场价）</summary>
    public decimal DefaultTrailingTakeProfitMultiplier { get; set; } = 1.15m;
    /// <summary>ATR止损倍数</summary>
    public decimal AtrMultiplier { get; set; } = 3;
}
