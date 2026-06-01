namespace AIStock.Core.Models;

/// <summary>
/// 板块内个股资金流（东财 clist b:{板块} 实时排行，按主力净流入排序）
/// </summary>
public class SectorStockFlow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal MainNetInflow { get; set; }      // 主力净流入额（元）
    public decimal MainNetRatio { get; set; }        // 主力净占比（%）
    public decimal SuperLargeOrderNet { get; set; }
    public decimal LargeOrderNet { get; set; }
    public decimal MediumOrderNet { get; set; }
    public decimal SmallOrderNet { get; set; }
}
