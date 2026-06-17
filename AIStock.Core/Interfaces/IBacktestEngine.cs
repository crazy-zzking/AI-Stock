using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 回测引擎接口（统一版）。
/// Strategy 引擎 <see cref="Strategy.Services.BacktestEngineService"/> 和 Selection 引擎共用此接口。
/// </summary>
public interface IBacktestEngine
{
    /// <summary>
    /// 运行多股票策略回测（原 Strategy 引擎 <see cref="RunBacktestAsync"/>）。
    /// </summary>
    Task<BacktestResult> RunBacktestAsync(BacktestConfig config, IStrategy strategy, List<string> codes, DateTime startTime, DateTime endTime);

    /// <summary>
    /// 运行单股票回测。
    /// </summary>
    Task<BacktestResult> RunSingleStockBacktestAsync(BacktestConfig config, IStrategy strategy, string code, DateTime startTime, DateTime endTime);
}
