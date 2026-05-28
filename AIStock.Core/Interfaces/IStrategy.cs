using AIStock.Core.Enums;
using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 策略接口
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 策略描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 生成交易信号
    /// </summary>
    Task<TradeSignal?> GenerateSignalAsync(string code, List<KlineData> klines, TechnicalIndicator indicators);

    /// <summary>
    /// 批量生成信号
    /// </summary>
    Task<List<TradeSignal>> GenerateSignalsAsync(List<string> codes, DateTime dateTime);
}
