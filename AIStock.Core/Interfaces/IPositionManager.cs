using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 持仓管理接口
/// </summary>
public interface IPositionManager
{
    /// <summary>
    /// 获取所有持仓
    /// </summary>
    Task<List<PortfolioPosition>> GetPositionsAsync();

    /// <summary>
    /// 获取指定股票持仓
    /// </summary>
    Task<PortfolioPosition?> GetPositionAsync(string code);

    /// <summary>
    /// 更新持仓
    /// </summary>
    Task UpdatePositionAsync(PortfolioPosition position);

    /// <summary>
    /// 删除持仓
    /// </summary>
    Task RemovePositionAsync(string code);

    /// <summary>
    /// 获取持仓汇总
    /// </summary>
    Task<PositionSummary> GetSummaryAsync();
}

/// <summary>
/// 持仓汇总
/// </summary>
public class PositionSummary
{
    /// <summary>
    /// 总市值
    /// </summary>
    public decimal TotalMarketValue { get; set; }

    /// <summary>
    /// 总盈亏
    /// </summary>
    public decimal TotalProfit { get; set; }

    /// <summary>
    /// 总盈亏比例
    /// </summary>
    public decimal TotalProfitRate { get; set; }

    /// <summary>
    /// 持仓数量
    /// </summary>
    public int PositionCount { get; set; }

    /// <summary>
    /// 盈利数量
    /// </summary>
    public int ProfitCount { get; set; }

    /// <summary>
    /// 亏损数量
    /// </summary>
    public int LossCount { get; set; }

    /// <summary>
    /// 持仓列表
    /// </summary>
    public List<PortfolioPosition> Positions { get; set; } = new();
}
