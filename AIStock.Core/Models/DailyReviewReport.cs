namespace AIStock.Core.Models;

/// <summary>
/// 每日复盘报告 — 收盘后基于已有数据源（快照/板块/龙虎榜/概念/事件/选股/订单）汇总，
/// 分析当日领涨板块、领涨个股及其涨因，并诊断数据完备性，最后给出文字总结。
/// </summary>
public class DailyReviewReport
{
    /// <summary>复盘所基于的交易日</summary>
    public DateTime TradingDate { get; set; }
    /// <summary>生成时间（UTC）</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>交易复盘（当日下单统计）</summary>
    public OrderReview Orders { get; set; } = new();
    /// <summary>市场宽度（涨跌家数/涨停等）</summary>
    public MarketBreadth Market { get; set; } = new();
    /// <summary>领涨板块 TOP</summary>
    public List<SectorReviewItem> TopSectors { get; set; } = new();
    /// <summary>领涨个股 TOP（含涨因归因）</summary>
    public List<StockReviewItem> TopStocks { get; set; } = new();
    /// <summary>当日热门题材（活跃股扎堆的概念）</summary>
    public List<HotThemeItem> HotThemes { get; set; } = new();
    /// <summary>选股回测（最近一次选股结果在当日的表现）</summary>
    public SelectionReview? Selection { get; set; }
    /// <summary>数据完备性诊断（哪些数据源缺失会影响复盘归因）</summary>
    public List<DataGapItem> DataGaps { get; set; } = new();
    /// <summary>文字总结</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>当日下单统计</summary>
public class OrderReview
{
    public int TotalOrders { get; set; }
    public int SuccessOrders { get; set; }
    public int FailedOrders { get; set; }
    public int BuyOrders { get; set; }
    public int SellOrders { get; set; }
    public decimal TotalValue { get; set; }
    public string GateMode { get; set; } = string.Empty;
}

/// <summary>市场宽度</summary>
public class MarketBreadth
{
    public int TotalCount { get; set; }
    public int UpCount { get; set; }
    public int DownCount { get; set; }
    public int FlatCount { get; set; }
    public int LimitUpCount { get; set; }
    /// <summary>当日全市场主力净流入合计（元）</summary>
    public decimal TotalMainNetInflow { get; set; }
    /// <summary>当日平均涨跌幅（%）</summary>
    public decimal AvgChangePercent { get; set; }
}

/// <summary>领涨板块</summary>
public class SectorReviewItem
{
    public string SectorCode { get; set; } = string.Empty;
    public string SectorName { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public decimal NetInflow { get; set; }
    /// <summary>板块内领涨个股（名称）</summary>
    public List<string> LeadingStocks { get; set; } = new();
    /// <summary>涨因（规则归因文字）</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>领涨个股（含归因）</summary>
public class StockReviewItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public decimal MainNetInflow { get; set; }
    public decimal TurnoverRate { get; set; }
    public bool IsLimitUp { get; set; }
    /// <summary>关联概念/题材</summary>
    public List<string> Concepts { get; set; } = new();
    /// <summary>命中当日热门题材的概念</summary>
    public List<string> HotConcepts { get; set; } = new();
    /// <summary>是否当日上龙虎榜</summary>
    public bool OnDragonTiger { get; set; }
    /// <summary>龙虎榜上榜原因</summary>
    public string? DragonTigerReason { get; set; }
    /// <summary>关联当日事件标题</summary>
    public List<string> Events { get; set; } = new();
    /// <summary>涨因（规则归因文字）</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>当日热门题材</summary>
public class HotThemeItem
{
    public string Concept { get; set; } = string.Empty;
    public int ActiveStockCount { get; set; }
    public List<string> LeadingStocks { get; set; } = new();
}

/// <summary>选股回测：最近一次选股结果在当日的表现</summary>
public class SelectionReview
{
    public DateTime SelectionRunAt { get; set; }
    public int Count { get; set; }
    /// <summary>上涨命中数（当日涨幅 &gt; 0）</summary>
    public int HitCount { get; set; }
    /// <summary>平均当日涨幅（%）</summary>
    public decimal AvgChangePercent { get; set; }
    public List<SelectionReviewItem> Items { get; set; } = new();
}

public class SelectionReviewItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>当日涨幅（%），无快照则为 null</summary>
    public decimal? ChangePercent { get; set; }
    public bool Hit { get; set; }
}

/// <summary>数据完备性诊断项</summary>
public class DataGapItem
{
    public string Source { get; set; } = string.Empty;
    public bool Available { get; set; }
    /// <summary>说明：缺失时影响哪部分复盘</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>复盘历史列表项（元信息）</summary>
public class DailyReviewSummaryItem
{
    public long Id { get; set; }
    public DateTime TradingDate { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int LimitUpCount { get; set; }
    public int UpCount { get; set; }
    public int DownCount { get; set; }
}
