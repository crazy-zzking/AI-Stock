namespace AIStock.Core.Models;

/// <summary>
/// 龙虎榜数据（个股某日上榜汇总）
/// </summary>
public class DragonTigerData
{
    /// <summary>交易日期</summary>
    public DateTime Date { get; set; }

    /// <summary>股票代码</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>股票名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>上榜原因（如"日涨幅偏离值达7%"）</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>龙虎榜净买入额（元，买方合计 - 卖方合计）</summary>
    public decimal NetBuyAmount { get; set; }

    /// <summary>买入合计（元）</summary>
    public decimal BuyAmount { get; set; }

    /// <summary>卖出合计（元）</summary>
    public decimal SellAmount { get; set; }

    /// <summary>买方是否含机构专用席位</summary>
    public bool HasInstitution { get; set; }

    /// <summary>买方席位</summary>
    public List<DragonTigerSeat> BuySeats { get; set; } = new();

    /// <summary>卖方席位</summary>
    public List<DragonTigerSeat> SellSeats { get; set; } = new();

    /// <summary>数据来源</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 龙虎榜席位（营业部/机构）
/// </summary>
public class DragonTigerSeat
{
    /// <summary>席位名称（营业部或"机构专用"）</summary>
    public string SeatName { get; set; } = string.Empty;

    /// <summary>买入额（元）</summary>
    public decimal BuyAmount { get; set; }

    /// <summary>卖出额（元）</summary>
    public decimal SellAmount { get; set; }

    /// <summary>是否机构专用席位</summary>
    public bool IsInstitution { get; set; }
}
