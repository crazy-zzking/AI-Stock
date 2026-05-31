using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIStock.Risk.Services;

/// <summary>
/// 风控引擎实现
/// </summary>
public class RiskEngineService : IRiskEngine
{
    private readonly RiskConfig _config;
    private readonly ILogger<RiskEngineService> _logger;

    public RiskEngineService(ILogger<RiskEngineService> logger) : this(logger, new RiskConfig()) { }

    public RiskEngineService(ILogger<RiskEngineService> logger, RiskConfig config)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<RiskCheckResult> CheckRiskAsync(TradeSignal signal, List<PortfolioPosition> positions, decimal totalCapital)
    {
        var result = new RiskCheckResult { Passed = true };

        // 极端行情检查：买入信号时，若跌幅 >= 9% 直接拒绝（接近跌停，流动性风险高）
        if (signal.SignalType is SignalType.Buy or SignalType.StrongBuy)
        {
            var extremeCheck = CheckExtremeDecline(signal);
            result.Checks.Add(extremeCheck);
            if (!extremeCheck.Passed) result.Passed = false;
        }

        var singleTradeCheck = await CheckSingleTradeAsync(signal, totalCapital);
        result.Checks.Add(singleTradeCheck);
        if (!singleTradeCheck.Passed) result.Passed = false;

        var totalPositionCheck = await CheckTotalPositionAsync(positions, totalCapital);
        result.Checks.Add(totalPositionCheck);
        if (!totalPositionCheck.Passed) result.Passed = false;

        var singleStockCheck = await CheckSingleStockPositionAsync(signal.Code, signal.Price * signal.Volume, totalCapital);
        result.Checks.Add(singleStockCheck);
        if (!singleStockCheck.Passed) result.Passed = false;

        var sectorCheck = await CheckSectorConcentrationAsync(positions, totalCapital);
        result.Checks.Add(sectorCheck);
        if (!sectorCheck.Passed) result.Passed = false;

        result.RiskLevel = CalculateRiskLevel(result.Checks);
        result.Suggestion = GenerateSuggestion(result);

        return result;
    }

    private RiskCheckItem CheckExtremeDecline(TradeSignal signal)
    {
        // 信号携带涨跌幅时才检查（ChangePercent 为 0 视为未知，跳过）
        var changePercent = signal.ChangePercent;
        var triggered = changePercent != 0 && changePercent <= -_config.MaxDeclinePercent;

        return new RiskCheckItem
        {
            Name = "极端下跌禁买",
            Passed = !triggered,
            CurrentValue = changePercent,
            LimitValue = -_config.MaxDeclinePercent,
            Description = triggered
                ? $"涨跌幅 {changePercent:F2}%，达到极端下跌阈值 -{_config.MaxDeclinePercent}%，禁止买入"
                : $"涨跌幅 {changePercent:F2}%，未触发极端下跌熔断"
        };
    }

    public async Task<RiskCheckItem> CheckSingleTradeAsync(TradeSignal signal, decimal totalCapital)
    {
        var tradeAmount = signal.Price * signal.Volume;
        var tradePercent = tradeAmount / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "单笔交易限制",
            Passed = tradePercent <= _config.MaxSingleTradePercent,
            CurrentValue = tradePercent,
            LimitValue = _config.MaxSingleTradePercent,
            Description = $"单笔交易占比: {tradePercent:F2}%，限制: {_config.MaxSingleTradePercent}%"
        };
    }

    public async Task<RiskCheckItem> CheckTotalPositionAsync(List<PortfolioPosition> positions, decimal totalCapital)
    {
        var totalPositionValue = positions.Sum(p => p.MarketValue);
        var totalPercent = totalPositionValue / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "总仓位限制",
            Passed = totalPercent <= _config.MaxTotalPositionPercent,
            CurrentValue = totalPercent,
            LimitValue = _config.MaxTotalPositionPercent,
            Description = $"总仓位占比: {totalPercent:F2}%，限制: {_config.MaxTotalPositionPercent}%"
        };
    }

    public async Task<RiskCheckItem> CheckSingleStockPositionAsync(string code, decimal positionValue, decimal totalCapital)
    {
        var positionPercent = positionValue / totalCapital * 100;

        return new RiskCheckItem
        {
            Name = "单票仓位限制",
            Passed = positionPercent <= _config.MaxSingleStockPercent,
            CurrentValue = positionPercent,
            LimitValue = _config.MaxSingleStockPercent,
            Description = $"单票仓位占比: {positionPercent:F2}%，限制: {_config.MaxSingleStockPercent}%"
        };
    }

    public async Task<RiskCheckItem> CheckSectorConcentrationAsync(List<PortfolioPosition> positions, decimal totalCapital)
    {
        // 按行业分组检查集中度
        var sectorGroups = positions
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .GroupBy(p => GetSectorFromPosition(p))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        var maxSectorPercent = 0m;
        var maxSectorName = "";

        foreach (var group in sectorGroups)
        {
            var sectorValue = group.Sum(p => p.MarketValue);
            var sectorPercent = sectorValue / totalCapital * 100;
            if (sectorPercent > maxSectorPercent)
            {
                maxSectorPercent = sectorPercent;
                maxSectorName = group.Key;
            }
        }

        return new RiskCheckItem
        {
            Name = "板块集中度限制",
            Passed = maxSectorPercent <= _config.MaxSectorPercent,
            CurrentValue = maxSectorPercent,
            LimitValue = _config.MaxSectorPercent,
            Description = $"板块集中度: {maxSectorPercent:F2}%（{maxSectorName}），限制: {_config.MaxSectorPercent}%"
        };
    }

    private static string GetSectorFromPosition(PortfolioPosition position)
    {
        if (!string.IsNullOrEmpty(position.Industry))
            return position.Industry;
        return "未知";
    }

    private RiskLevel CalculateRiskLevel(List<RiskCheckItem> checks)
    {
        var failedCount = checks.Count(c => !c.Passed);

        if (failedCount >= _config.CriticalThreshold) return RiskLevel.Critical;
        if (failedCount >= _config.HighThreshold) return RiskLevel.High;
        if (failedCount >= _config.MediumThreshold) return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private string GenerateSuggestion(RiskCheckResult result)
    {
        if (result.Passed)
        {
            return "风控检查通过，可以执行交易";
        }

        var suggestions = new List<string>();

        foreach (var check in result.Checks.Where(c => !c.Passed))
        {
            switch (check.Name)
            {
                case "单笔交易限制":
                    suggestions.Add("建议减小单笔交易金额");
                    break;
                case "总仓位限制":
                    suggestions.Add("建议降低总仓位");
                    break;
                case "单票仓位限制":
                    suggestions.Add("建议减小单票仓位");
                    break;
                case "板块集中度限制":
                    suggestions.Add("建议分散投资，降低板块集中度");
                    break;
            }
        }

        return string.Join("；", suggestions);
    }
}
