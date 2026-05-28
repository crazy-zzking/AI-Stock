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
    Task<List<AccountPosition>> GetAccountPositionsAsync();

    /// <summary>
    /// 获取账户资金
    /// </summary>
    Task<AccountBalance?> GetAccountBalanceAsync();
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

/// <summary>
/// 账户资金
/// </summary>
public class AccountBalance
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
    /// 持仓市值
    /// </summary>
    public decimal PositionValue { get; set; }

    /// <summary>
    /// 冻结资金
    /// </summary>
    public decimal FrozenBalance { get; set; }
}
