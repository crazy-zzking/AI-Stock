using AIStock.Core.Enums;
using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 数据源提供者统一接口
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// 提供者ID
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// 提供者名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 支持的能力列表
    /// </summary>
    IEnumerable<DataCapability> Capabilities { get; }

    /// <summary>
    /// 获取实时行情
    /// </summary>
    Task<QuoteData?> GetQuoteAsync(string code);

    /// <summary>
    /// 批量获取实时行情
    /// </summary>
    Task<List<QuoteData>> GetQuotesAsync(IEnumerable<string> codes);

    /// <summary>
    /// 获取K线数据
    /// </summary>
    Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100);

    /// <summary>
    /// 获取分时数据
    /// </summary>
    Task<List<IntradayData>> GetIntradayAsync(string code);

    /// <summary>
    /// 获取历史分时数据
    /// </summary>
    Task<List<IntradayData>> GetHistoryIntradayAsync(string date, string code);

    /// <summary>
    /// 获取分笔成交数据
    /// </summary>
    Task<List<TradeData>> GetTradesAsync(string code, int start = 0, int count = 100);

    /// <summary>
    /// 获取历史分笔成交数据
    /// </summary>
    Task<List<TradeData>> GetHistoryTradesAsync(string date, string code, int start = 0, int count = 100);

    /// <summary>
    /// 获取集合竞价数据
    /// </summary>
    Task<List<CallAuctionData>> GetCallAuctionAsync(string code);

    /// <summary>
    /// 获取股票代码列表
    /// </summary>
    Task<List<StockCodeInfo>> GetStockCodesAsync(string market);

    /// <summary>
    /// 获取股票代码列表（按类型过滤）
    /// </summary>
    Task<List<StockCodeInfo>> GetStockCodesAsync(string market, SecurityType securityType);

    /// <summary>
    /// 获取资金流向
    /// </summary>
    Task<CapitalFlowData?> GetCapitalFlowAsync(string code);

    /// <summary>
    /// 获取股票列表
    /// </summary>
    Task<List<StockInfo>> GetStockListAsync();

    /// <summary>
    /// 检查是否为交易日
    /// </summary>
    Task<bool> IsTradingDayAsync(DateTime date);

    /// <summary>
    /// 健康检查
    /// </summary>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// 获取账户持仓
    /// </summary>
    Task<AccountInfo> GetAccountInfoAsync();

    /// <summary>
    /// 买入下单
    /// </summary>
    Task<TradingOrderResult> PlaceBuyOrderAsync(string code, decimal price, int volume) =>
        throw new NotSupportedException("This provider does not support trading");

    /// <summary>
    /// 卖出下单
    /// </summary>
    Task<TradingOrderResult> PlaceSellOrderAsync(string code, decimal price, int volume) =>
        throw new NotSupportedException("This provider does not support trading");

    /// <summary>
    /// 查询订单状态
    /// </summary>
    Task<TradingOrderStatus?> QueryOrderAsync(long orderId) =>
        throw new NotSupportedException("This provider does not support trading");

    /// <summary>
    /// 获取今日成交
    /// </summary>
    Task<List<TradingTradeInfo>> GetTodayTradesAsync() =>
        throw new NotSupportedException("This provider does not support trading");
}

/// <summary>
/// 交易订单结果
/// </summary>
public record TradingOrderResult
{
    public long OrderId { get; init; }
    public bool IsAccepted { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsFailed { get; init; }
    public string Msg { get; init; } = string.Empty;
}

/// <summary>
/// 交易订单状态
/// </summary>
public record TradingOrderStatus
{
    public long OrderId { get; init; }
    public bool IsPending { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsFailed { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// 交易成交信息
/// </summary>
public record TradingTradeInfo
{
    public long AgreeId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Volume { get; init; }
    public DateTime TradeTime { get; init; }
}

/// <summary>
/// 账户信息
/// </summary>
public class AccountInfo
{
    /// <summary>
    /// 总资产
    /// </summary>
    public decimal TotalAssets { get; set; }

    /// <summary>
    /// 可用资金
    /// </summary>
    public decimal AvailableBalance { get; set; }

    /// <summary>
    /// 总盈亏
    /// </summary>
    public decimal TotalProfit { get; set; }

    /// <summary>
    /// 持仓列表
    /// </summary>
    public List<AccountPosition> Positions { get; set; } = new();
}

/// <summary>
/// 账户持仓
/// </summary>
public class AccountPosition
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 持仓数量
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 可用数量
    /// </summary>
    public long AvailableVolume { get; set; }

    /// <summary>
    /// 成本价
    /// </summary>
    public decimal CostPrice { get; set; }

    /// <summary>
    /// 现价
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 市值
    /// </summary>
    public decimal MarketValue { get; set; }

    /// <summary>
    /// 盈亏金额
    /// </summary>
    public decimal Profit { get; set; }

    /// <summary>
    /// 盈亏比例
    /// </summary>
    public decimal ProfitRate { get; set; }
}
