using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Risk.Services;

/// <summary>
/// 仓位管理实现
/// </summary>
public class PositionSizerService : IPositionSizer
{
    private readonly ILogger<PositionSizerService> _logger;

    public PositionSizerService(ILogger<PositionSizerService> logger)
    {
        _logger = logger;
    }

    public decimal CalculatePositionSize(TradeSignal signal, decimal totalCapital, PositionSizeMode mode)
    {
        switch (mode)
        {
            case PositionSizeMode.Kelly:
                return CalculateKellyPosition(signal, totalCapital);
            case PositionSizeMode.RiskParity:
                return CalculateRiskParityPosition(signal, totalCapital);
            case PositionSizeMode.VolatilityTarget:
                return CalculateVolatilityTargetPosition(signal, totalCapital);
            case PositionSizeMode.FixedRatio:
                return totalCapital * 0.1m;
            case PositionSizeMode.EqualWeight:
                return totalCapital * 0.1m;
            default:
                return totalCapital * 0.1m;
        }
    }

    public decimal CalculateKelly(decimal winRate, decimal profitLossRatio)
    {
        if (profitLossRatio <= 0 || winRate <= 0 || winRate >= 1)
            return 0;

        var kelly = winRate - (1 - winRate) / profitLossRatio;
        return Math.Max(0, Math.Min(0.5m, kelly));
    }

    public decimal CalculateRiskParity(decimal volatility, decimal targetRisk)
    {
        if (volatility <= 0)
            return 0;

        return targetRisk / volatility;
    }

    public decimal CalculateVolatilityTarget(decimal volatility, decimal targetVolatility, decimal totalCapital)
    {
        if (volatility <= 0)
            return 0;

        var targetPosition = totalCapital * (targetVolatility / volatility);
        return Math.Min(totalCapital * 0.5m, targetPosition);
    }

    private decimal CalculateKellyPosition(TradeSignal signal, decimal totalCapital)
    {
        var winRate = signal.Strength / 100m;
        var profitLossRatio = 1.5m;

        var kelly = CalculateKelly(winRate, profitLossRatio);
        var halfKelly = kelly / 2;

        return totalCapital * halfKelly;
    }

    private decimal CalculateRiskParityPosition(TradeSignal signal, decimal totalCapital)
    {
        var assumedVolatility = 0.3m;
        var targetRisk = 0.15m;

        var positionSize = CalculateRiskParity(assumedVolatility, targetRisk);
        return totalCapital * positionSize;
    }

    private decimal CalculateVolatilityTargetPosition(TradeSignal signal, decimal totalCapital)
    {
        var assumedVolatility = 0.3m;
        var targetVolatility = 0.15m;

        return CalculateVolatilityTarget(assumedVolatility, targetVolatility, totalCapital);
    }
}
