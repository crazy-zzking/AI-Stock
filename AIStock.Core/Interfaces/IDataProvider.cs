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
    /// <param name="code">股票代码</param>
    /// <returns>行情数据</returns>
    Task<QuoteData?> GetQuoteAsync(string code);

    /// <summary>
    /// 批量获取实时行情
    /// </summary>
    /// <param name="codes">股票代码列表</param>
    /// <returns>行情数据列表</returns>
    Task<List<QuoteData>> GetQuotesAsync(IEnumerable<string> codes);

    /// <summary>
    /// 获取K线数据
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <param name="interval">K线周期</param>
    /// <param name="count">数量</param>
    /// <returns>K线数据列表</returns>
    Task<List<KlineData>> GetKlinesAsync(string code, KlineInterval interval, int count = 100);

    /// <summary>
    /// 获取分时数据
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <returns>分时数据列表</returns>
    Task<List<IntradayData>> GetIntradayAsync(string code);

    /// <summary>
    /// 获取资金流向
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <returns>资金流向数据</returns>
    Task<CapitalFlowData?> GetCapitalFlowAsync(string code);

    /// <summary>
    /// 获取股票列表
    /// </summary>
    /// <returns>股票列表</returns>
    Task<List<StockInfo>> GetStockListAsync();

    /// <summary>
    /// 检查是否为交易日
    /// </summary>
    /// <param name="date">日期</param>
    /// <returns>是否为交易日</returns>
    Task<bool> IsTradingDayAsync(DateTime date);

    /// <summary>
    /// 健康检查
    /// </summary>
    /// <returns>是否健康</returns>
    Task<bool> IsHealthyAsync();
}
