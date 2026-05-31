namespace AIStock.Core.Models;

/// <summary>
/// 板块资金流向统一模型
/// </summary>
public class SectorFlowData
{
    public string SectorCode { get; set; } = string.Empty;
    public string SectorName { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public decimal Price { get; set; }
    public decimal NetInflow { get; set; }
    public decimal SuperLargeOrderNet { get; set; }
    public decimal LargeOrderNet { get; set; }
    public decimal MediumOrderNet { get; set; }
    public decimal SmallOrderNet { get; set; }
    public decimal TurnoverAmount { get; set; }
}
