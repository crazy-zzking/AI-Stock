using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// Alpha引擎接口 - 交易信号生成
/// </summary>
public interface IAlphaEngine
{
    /// <summary>
    /// 生成综合信号
    /// </summary>
    Task<TradeSignal> GenerateCompositeSignalAsync(string code, List<TradeSignal> signals);

    /// <summary>
    /// 多策略信号融合
    /// </summary>
    Task<TradeSignal> MergeSignalsAsync(List<TradeSignal> signals);

    /// <summary>
    /// 获取信号置信度
    /// </summary>
    Task<decimal> CalculateConfidenceAsync(TradeSignal signal);
}
