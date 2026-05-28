namespace AIStock.Core.Models;

/// <summary>
/// 技术指标
/// </summary>
public class TechnicalIndicator
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 日期时间
    /// </summary>
    public DateTime DateTime { get; set; }

    /// <summary>
    /// MA指标
    /// </summary>
    public MAIndicator? MA { get; set; }

    /// <summary>
    /// MACD指标
    /// </summary>
    public MACDIndicator? MACD { get; set; }

    /// <summary>
    /// RSI指标
    /// </summary>
    public RSIIndicator? RSI { get; set; }

    /// <summary>
    /// VWAP指标
    /// </summary>
    public decimal? VWAP { get; set; }

    /// <summary>
    /// 波动率
    /// </summary>
    public decimal? Volatility { get; set; }

    /// <summary>
    /// ATR指标
    /// </summary>
    public decimal? ATR { get; set; }
}

/// <summary>
/// MA指标
/// </summary>
public class MAIndicator
{
    /// <summary>
    /// MA5
    /// </summary>
    public decimal? MA5 { get; set; }

    /// <summary>
    /// MA10
    /// </summary>
    public decimal? MA10 { get; set; }

    /// <summary>
    /// MA20
    /// </summary>
    public decimal? MA20 { get; set; }

    /// <summary>
    /// MA60
    /// </summary>
    public decimal? MA60 { get; set; }

    /// <summary>
    /// MA120
    /// </summary>
    public decimal? MA120 { get; set; }

    /// <summary>
    /// MA250
    /// </summary>
    public decimal? MA250 { get; set; }
}

/// <summary>
/// MACD指标
/// </summary>
public class MACDIndicator
{
    /// <summary>
    /// DIF
    /// </summary>
    public decimal DIF { get; set; }

    /// <summary>
    /// DEA
    /// </summary>
    public decimal DEA { get; set; }

    /// <summary>
    /// MACD柱
    /// </summary>
    public decimal MACD { get; set; }
}

/// <summary>
/// RSI指标
/// </summary>
public class RSIIndicator
{
    /// <summary>
    /// RSI6
    /// </summary>
    public decimal RSI6 { get; set; }

    /// <summary>
    /// RSI12
    /// </summary>
    public decimal RSI12 { get; set; }

    /// <summary>
    /// RSI24
    /// </summary>
    public decimal RSI24 { get; set; }
}

/// <summary>
/// 交易信号
/// </summary>
public class TradeSignal
{
    /// <summary>
    /// 信号ID
    /// </summary>
    public string SignalId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 信号类型（buy/sell/hold）
    /// </summary>
    public string SignalType { get; set; } = string.Empty;

    /// <summary>
    /// 信号强度（0-100）
    /// </summary>
    public int Strength { get; set; }

    /// <summary>
    /// 信号价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 策略名称
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// 信号原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 信号时间
    /// </summary>
    public DateTime SignalTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 止损价
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// 止盈价
    /// </summary>
    public decimal? TakeProfitPrice { get; set; }
}

/// <summary>
/// 回测结果
/// </summary>
public class BacktestResult
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 初始资金
    /// </summary>
    public decimal InitialCapital { get; set; }

    /// <summary>
    /// 最终资金
    /// </summary>
    public decimal FinalCapital { get; set; }

    /// <summary>
    /// 总收益率（%）
    /// </summary>
    public decimal TotalReturn { get; set; }

    /// <summary>
    /// 年化收益率（%）
    /// </summary>
    public decimal AnnualizedReturn { get; set; }

    /// <summary>
    /// 最大回撤（%）
    /// </summary>
    public decimal MaxDrawdown { get; set; }

    /// <summary>
    /// 夏普比率
    /// </summary>
    public decimal SharpeRatio { get; set; }

    /// <summary>
    /// 胜率（%）
    /// </summary>
    public decimal WinRate { get; set; }

    /// <summary>
    /// 盈亏比
    /// </summary>
    public decimal ProfitLossRatio { get; set; }

    /// <summary>
    /// 交易次数
    /// </summary>
    public int TradeCount { get; set; }

    /// <summary>
    /// 交易列表
    /// </summary>
    public List<BacktestTrade> Trades { get; set; } = new();

    /// <summary>
    /// 每日净值
    /// </summary>
    public List<DailyNav> DailyNavs { get; set; } = new();
}

/// <summary>
/// 回测交易
/// </summary>
public class BacktestTrade
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 买入时间
    /// </summary>
    public DateTime BuyTime { get; set; }

    /// <summary>
    /// 买入价格
    /// </summary>
    public decimal BuyPrice { get; set; }

    /// <summary>
    /// 卖出时间
    /// </summary>
    public DateTime? SellTime { get; set; }

    /// <summary>
    /// 卖出价格
    /// </summary>
    public decimal? SellPrice { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public long Volume { get; set; }

    /// <summary>
    /// 盈亏金额
    /// </summary>
    public decimal Profit { get; set; }

    /// <summary>
    /// 盈亏比例（%）
    /// </summary>
    public decimal ProfitRate { get; set; }

    /// <summary>
    /// 手续费
    /// </summary>
    public decimal Commission { get; set; }
}

/// <summary>
/// 每日净值
/// </summary>
public class DailyNav
{
    /// <summary>
    /// 日期
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 净值
    /// </summary>
    public decimal Nav { get; set; }

    /// <summary>
    /// 收益率（%）
    /// </summary>
    public decimal Return { get; set; }
}

/// <summary>
/// 持仓信息
/// </summary>
public class PortfolioPosition
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
    /// 盈亏比例（%）
    /// </summary>
    public decimal ProfitRate { get; set; }

    /// <summary>
    /// 仓位比例（%）
    /// </summary>
    public decimal Weight { get; set; }

    /// <summary>
    /// 止损价
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// 止盈价
    /// </summary>
    public decimal? TakeProfitPrice { get; set; }
}

/// <summary>
/// 风控检查结果
/// </summary>
public class RiskCheckResult
{
    /// <summary>
    /// 是否通过
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// 检查项
    /// </summary>
    public List<RiskCheckItem> Checks { get; set; } = new();

    /// <summary>
    /// 风险等级（low/medium/high/critical）
    /// </summary>
    public string RiskLevel { get; set; } = "low";

    /// <summary>
    /// 建议
    /// </summary>
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// 风控检查项
/// </summary>
public class RiskCheckItem
{
    /// <summary>
    /// 检查名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否通过
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// 当前值
    /// </summary>
    public decimal CurrentValue { get; set; }

    /// <summary>
    /// 限制值
    /// </summary>
    public decimal LimitValue { get; set; }

    /// <summary>
    /// 说明
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
