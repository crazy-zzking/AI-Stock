using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 风控引擎接口
/// </summary>
public interface IRiskEngine
{
    /// <summary>
    /// 风控检查
    /// </summary>
    Task<RiskCheckResult> CheckRiskAsync(TradeSignal signal, List<PortfolioPosition> positions, decimal totalCapital);

    /// <summary>
    /// 检查单笔交易
    /// </summary>
    Task<RiskCheckItem> CheckSingleTradeAsync(TradeSignal signal, decimal totalCapital);

    /// <summary>
    /// 检查总仓位
    /// </summary>
    Task<RiskCheckItem> CheckTotalPositionAsync(List<PortfolioPosition> positions, decimal totalCapital);

    /// <summary>
    /// 检查单票仓位
    /// </summary>
    Task<RiskCheckItem> CheckSingleStockPositionAsync(string code, decimal positionValue, decimal totalCapital);

    /// <summary>
    /// 检查板块集中度
    /// </summary>
    Task<RiskCheckItem> CheckSectorConcentrationAsync(List<PortfolioPosition> positions, decimal totalCapital);
}
