using System.ComponentModel.DataAnnotations;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 风控管理API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RiskController : ControllerBase
{
    private readonly IRiskEngine _riskEngine;
    private readonly IPositionSizer _positionSizer;
    private readonly IStopLossSystem _stopLossSystem;
    private readonly ILogger<RiskController> _logger;

    public RiskController(
        IRiskEngine riskEngine,
        IPositionSizer positionSizer,
        IStopLossSystem stopLossSystem,
        ILogger<RiskController> logger)
    {
        _riskEngine = riskEngine;
        _positionSizer = positionSizer;
        _stopLossSystem = stopLossSystem;
        _logger = logger;
    }

    /// <summary>
    /// 风控检查
    /// </summary>
    [HttpPost("check")]
    public async Task<ActionResult<RiskCheckResult>> CheckRisk([FromBody] RiskCheckRequest request)
    {
        try
        {
            var result = await _riskEngine.CheckRiskAsync(
                request.Signal, request.Positions, request.TotalCapital);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check risk");
            return StatusCode(500, "Failed to check risk");
        }
    }

    /// <summary>
    /// 计算仓位大小
    /// </summary>
    [HttpPost("position-size")]
    public async Task<ActionResult<decimal>> CalculatePositionSize([FromBody] PositionSizeRequest request)
    {
        try
        {
            var size = _positionSizer.CalculatePositionSize(
                request.Signal, request.TotalCapital, request.Mode);
            return Ok(size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate position size");
            return StatusCode(500, "Failed to calculate position size");
        }
    }

    /// <summary>
    /// 计算Kelly公式
    /// </summary>
    [HttpPost("kelly")]
    public async Task<ActionResult<decimal>> CalculateKelly([FromBody] KellyRequest request)
    {
        var kelly = _positionSizer.CalculateKelly(request.WinRate, request.ProfitLossRatio);
        return Ok(kelly);
    }

    /// <summary>
    /// 计算止损价
    /// </summary>
    [HttpPost("stop-loss")]
    public async Task<ActionResult<decimal>> CalculateStopLoss([FromBody] StopLossRequest request)
    {
        try
        {
            var stopLoss = _stopLossSystem.CalculateStopLoss(
                request.Signal, request.Klines, request.Mode);
            return Ok(stopLoss);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate stop loss");
            return StatusCode(500, "Failed to calculate stop loss");
        }
    }

    /// <summary>
    /// 计算止盈价
    /// </summary>
    [HttpPost("take-profit")]
    public async Task<ActionResult<decimal>> CalculateTakeProfit([FromBody] StopLossRequest request)
    {
        try
        {
            var takeProfit = _stopLossSystem.CalculateTakeProfit(
                request.Signal, request.Klines, request.Mode);
            return Ok(takeProfit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate take profit");
            return StatusCode(500, "Failed to calculate take profit");
        }
    }

    /// <summary>
    /// ATR止损计算
    /// </summary>
    [HttpPost("atr-stop-loss")]
    public async Task<ActionResult<decimal>> CalculateATRStopLoss([FromBody] ATRStopLossRequest request)
    {
        var stopLoss = _stopLossSystem.CalculateATRStopLoss(
            request.EntryPrice, request.ATR, request.Multiplier);
        return Ok(stopLoss);
    }

    /// <summary>
    /// 固定止损计算
    /// </summary>
    [HttpPost("fixed-stop-loss")]
    public async Task<ActionResult<decimal>> CalculateFixedStopLoss([FromBody] FixedStopLossRequest request)
    {
        var stopLoss = _stopLossSystem.CalculateFixedStopLoss(
            request.EntryPrice, request.StopPercent);
        return Ok(stopLoss);
    }

    /// <summary>
    /// 动态止盈计算
    /// </summary>
    [HttpPost("trailing-stop")]
    public async Task<ActionResult<decimal>> CalculateTrailingStop([FromBody] TrailingStopRequest request)
    {
        var stopPrice = _stopLossSystem.CalculateTrailingStop(
            request.HighestPrice, request.TrailingPercent);
        return Ok(stopPrice);
    }
}

/// <summary>
/// 风控检查请求
/// </summary>
public class RiskCheckRequest
{
    /// <summary>
    /// 交易信号
    /// </summary>
    [Required(ErrorMessage = "交易信号不能为空")]
    public TradeSignal Signal { get; set; } = new();

    /// <summary>
    /// 当前持仓
    /// </summary>
    public List<PortfolioPosition> Positions { get; set; } = new();

    /// <summary>
    /// 总资金
    /// </summary>
    [Range(1, double.MaxValue, ErrorMessage = "总资金必须大于0")]
    public decimal TotalCapital { get; set; }
}

public class PositionSizeRequest
{
    [Required(ErrorMessage = "交易信号不能为空")]
    public TradeSignal Signal { get; set; } = new();

    [Range(1, double.MaxValue, ErrorMessage = "总资金必须大于0")]
    public decimal TotalCapital { get; set; }

    public PositionSizeMode Mode { get; set; } = PositionSizeMode.EqualWeight;
}

public class KellyRequest
{
    [Range(0.01, 0.99, ErrorMessage = "胜率必须在0.01-0.99之间")]
    public decimal WinRate { get; set; }

    [Range(0.01, 100, ErrorMessage = "盈亏比必须大于0")]
    public decimal ProfitLossRatio { get; set; }
}

public class StopLossRequest
{
    [Required(ErrorMessage = "交易信号不能为空")]
    public TradeSignal Signal { get; set; } = new();

    public List<KlineData> Klines { get; set; } = new();

    public StopLossMode Mode { get; set; } = StopLossMode.ATR;
}

public class ATRStopLossRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "入场价格必须大于0")]
    public decimal EntryPrice { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "ATR值必须大于0")]
    public decimal ATR { get; set; }

    [Range(0.1, 10, ErrorMessage = "倍数必须在0.1-10之间")]
    public decimal Multiplier { get; set; } = 2;
}

public class FixedStopLossRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "入场价格必须大于0")]
    public decimal EntryPrice { get; set; }

    [Range(0.01, 99.99, ErrorMessage = "止损百分比必须在0.01-99.99之间")]
    public decimal StopPercent { get; set; }
}

public class TrailingStopRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "最高价格必须大于0")]
    public decimal HighestPrice { get; set; }

    [Range(0.01, 99.99, ErrorMessage = "动态百分比必须在0.01-99.99之间")]
    public decimal TrailingPercent { get; set; }
}
