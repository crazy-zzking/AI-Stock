namespace AIStock.Core.Enums;

/// <summary>
/// 数据能力枚举
/// </summary>
public enum DataCapability
{
    /// <summary>
    /// 实时行情
    /// </summary>
    Quote,

    /// <summary>
    /// K线数据
    /// </summary>
    Kline,

    /// <summary>
    /// 分时数据
    /// </summary>
    Intraday,

    /// <summary>
    /// 历史分时
    /// </summary>
    HistoryIntraday,

    /// <summary>
    /// 分笔成交
    /// </summary>
    Trades,

    /// <summary>
    /// 历史分笔成交
    /// </summary>
    HistoryTrades,

    /// <summary>
    /// 集合竞价
    /// </summary>
    CallAuction,

    /// <summary>
    /// 资金流向
    /// </summary>
    CapitalFlow,

    /// <summary>
    /// 板块排名
    /// </summary>
    SectorRanking,

    /// <summary>
    /// 板块成分
    /// </summary>
    SectorConstituents,

    /// <summary>
    /// 交易日历
    /// </summary>
    TradingCalendar,

    /// <summary>
    /// 股票搜索
    /// </summary>
    StockSearch,

    /// <summary>
    /// 股票池
    /// </summary>
    StockUniverse
}
