using System.ComponentModel.DataAnnotations;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Strategy.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 策略回测API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StrategyController : ControllerBase
{
    private readonly IBacktestEngine _backtestEngine;
    private readonly IAlphaEngine _alphaEngine;
    private readonly IPortfolioEngine _portfolioEngine;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StrategyController> _logger;

    public StrategyController(
        IBacktestEngine backtestEngine,
        IAlphaEngine alphaEngine,
        IPortfolioEngine portfolioEngine,
        IServiceProvider serviceProvider,
        ILogger<StrategyController> logger)
    {
        _backtestEngine = backtestEngine;
        _alphaEngine = alphaEngine;
        _portfolioEngine = portfolioEngine;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// 运行回测
    /// </summary>
    [HttpPost("backtest")]
    public async Task<ActionResult<BacktestResult>> RunBacktest([FromBody] BacktestRequest request)
    {
        try
        {
            var config = new BacktestConfig
            {
                InitialCapital = request.InitialCapital,
                CommissionRate = request.CommissionRate,
                Slippage = request.Slippage,
                MaxPositionPercent = request.MaxPositionPercent,
                MaxPositions = request.MaxPositions
            };

            var strategy = GetStrategy(request.StrategyName);
            if (strategy == null)
            {
                return BadRequest($"Unknown strategy: {request.StrategyName}");
            }

            var result = await _backtestEngine.RunBacktestAsync(
                config, strategy, request.Codes, request.StartTime, request.EndTime);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run backtest");
            return StatusCode(500, "Failed to run backtest");
        }
    }

    /// <summary>
    /// 信号融合
    /// </summary>
    [HttpPost("signal/merge")]
    public async Task<ActionResult<TradeSignal>> MergeSignals([FromBody] List<TradeSignal> signals)
    {
        try
        {
            var mergedSignal = await _alphaEngine.MergeSignalsAsync(signals);
            return Ok(mergedSignal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to merge signals");
            return StatusCode(500, "Failed to merge signals");
        }
    }

    /// <summary>
    /// 计算目标仓位
    /// </summary>
    [HttpPost("portfolio/positions")]
    public async Task<ActionResult<List<PortfolioPosition>>> CalculatePositions(
        [FromBody] PortfolioRequest request)
    {
        try
        {
            var positions = await _portfolioEngine.CalculateTargetPositionsAsync(
                request.Signals, request.TotalCapital);
            return Ok(positions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate positions");
            return StatusCode(500, "Failed to calculate positions");
        }
    }

    /// <summary>
    /// 组合再平衡
    /// </summary>
    [HttpPost("portfolio/rebalance")]
    public async Task<ActionResult<List<TradeSignal>>> Rebalance([FromBody] RebalanceRequest request)
    {
        try
        {
            var signals = await _portfolioEngine.RebalanceAsync(
                request.CurrentPositions, request.TargetPositions);
            return Ok(signals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebalance");
            return StatusCode(500, "Failed to rebalance");
        }
    }

    private IStrategy? GetStrategy(string strategyName)
    {
        return strategyName.ToLower() switch
        {
            "mabreakout" => _serviceProvider.GetService<MABreakoutStrategy>(),
            "gridtrading" => _serviceProvider.GetService<GridTradingStrategy>(),
            "pairtrading" => _serviceProvider.GetService<PairTradingStrategy>(),
            _ => null
        };
    }
}

/// <summary>
/// 回测请求
/// </summary>
public class BacktestRequest
{
    /// <summary>
    /// 策略名称
    /// </summary>
    [Required(ErrorMessage = "策略名称不能为空")]
    public string StrategyName { get; set; } = "MABreakout";

    /// <summary>
    /// 股票代码列表
    /// </summary>
    [Required(ErrorMessage = "股票代码列表不能为空")]
    [MinLength(1, ErrorMessage = "至少需要一个股票代码")]
    public List<string> Codes { get; set; } = new();

    /// <summary>
    /// 开始时间
    /// </summary>
    [Required(ErrorMessage = "开始时间不能为空")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    [Required(ErrorMessage = "结束时间不能为空")]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 初始资金
    /// </summary>
    [Range(1, double.MaxValue, ErrorMessage = "初始资金必须大于0")]
    public decimal InitialCapital { get; set; } = 1000000;

    /// <summary>
    /// 手续费率（%）
    /// </summary>
    [Range(0, 100, ErrorMessage = "手续费率必须在0-100之间")]
    public decimal CommissionRate { get; set; } = 0.03m;

    /// <summary>
    /// 滑点（%）
    /// </summary>
    [Range(0, 100, ErrorMessage = "滑点必须在0-100之间")]
    public decimal Slippage { get; set; } = 0.1m;

    /// <summary>
    /// 单笔最大仓位比例（%）
    /// </summary>
    [Range(0.01, 100, ErrorMessage = "最大仓位比例必须在0.01-100之间")]
    public decimal MaxPositionPercent { get; set; } = 10;

    /// <summary>
    /// 最大持仓数量
    /// </summary>
    [Range(1, 1000, ErrorMessage = "最大持仓数量必须在1-1000之间")]
    public int MaxPositions { get; set; } = 10;
}

/// <summary>
/// 组合请求
/// </summary>
public class PortfolioRequest
{
    /// <summary>
    /// 交易信号列表
    /// </summary>
    public List<TradeSignal> Signals { get; set; } = new();

    /// <summary>
    /// 总资金
    /// </summary>
    public decimal TotalCapital { get; set; }
}

/// <summary>
/// 再平衡请求
/// </summary>
public class RebalanceRequest
{
    /// <summary>
    /// 当前持仓
    /// </summary>
    public List<PortfolioPosition> CurrentPositions { get; set; } = new();

    /// <summary>
    /// 目标持仓
    /// </summary>
    public List<PortfolioPosition> TargetPositions { get; set; } = new();
}
