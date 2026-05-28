using AIStock.Core.Enums;
using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 市场状态检测接口
/// </summary>
public interface IMarketStateDetector
{
    /// <summary>
    /// 检测市场状态
    /// </summary>
    Task<MarketState> DetectMarketStateAsync(string indexCode = "000001");

    /// <summary>
    /// 获取市场情绪指标
    /// </summary>
    Task<MarketSentiment> GetMarketSentimentAsync();

    /// <summary>
    /// 检测极端行情
    /// </summary>
    Task<bool> IsExtremeMarketAsync();
}

/// <summary>
/// 市场情绪
/// </summary>
public class MarketSentiment
{
    /// <summary>
    /// 涨跌比
    /// </summary>
    public decimal AdvanceDeclineRatio { get; set; }

    /// <summary>
    /// 涨停数量
    /// </summary>
    public int LimitUpCount { get; set; }

    /// <summary>
    /// 跌停数量
    /// </summary>
    public int LimitDownCount { get; set; }

    /// <summary>
    /// 成交额（亿）
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// 北向资金净流入（亿）
    /// </summary>
    public decimal NorthboundFlow { get; set; }

    /// <summary>
    /// 情绪得分（0-100）
    /// </summary>
    public int SentimentScore { get; set; }
}
