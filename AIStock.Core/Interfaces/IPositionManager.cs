using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 持仓管理接口
/// </summary>
public interface IPositionManager
{
    /// <summary>
    /// 获取持仓和账户信息
    /// </summary>
    Task<PositionSummary> GetPositionSummaryAsync();

    /// <summary>
    /// 获取指定股票持仓
    /// </summary>
    Task<PortfolioPosition?> GetPositionAsync(string code);

    /// <summary>
    /// 强制刷新持仓缓存（从 Provider 重新拉取并写入 Redis）
    /// </summary>
    Task<PositionSummary> RefreshAsync();
}

/// <summary>
/// 持仓汇总
/// </summary>
public class PositionSummary
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
    /// 缓存更新时间（UTC），null 表示直连 Provider 未缓存
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 持仓列表
    /// </summary>
    public List<PortfolioPosition> Positions { get; set; } = new();
}
