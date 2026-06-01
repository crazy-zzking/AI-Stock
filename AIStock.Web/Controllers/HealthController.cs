using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace AIStock.Web.Controllers;

/// <summary>
/// 健康检查API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ITradingGate _tradingGate;
    private readonly IPositionManager _positionManager;
    private readonly IDataProviderResolver _dataProviderResolver;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IConnectionMultiplexer redis,
        ITradingGate tradingGate,
        IPositionManager positionManager,
        IDataProviderResolver dataProviderResolver,
        ILogger<HealthController> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _tradingGate = tradingGate;
        _positionManager = positionManager;
        _dataProviderResolver = dataProviderResolver;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    [HttpGet]
    public ActionResult<object> Get()
    {
        try
        {
            var redisConnected = _redis.IsConnected;
            var serverTime = DateTime.Now;

            return Ok(new
            {
                Status = "Healthy",
                Timestamp = serverTime,
                Redis = new
                {
                    Connected = redisConnected
                },
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(500, new
            {
                Status = "Unhealthy",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// 就绪检查
    /// </summary>
    [HttpGet("ready")]
    public ActionResult Ready()
    {
        try
        {
            var redisConnected = _redis.IsConnected;
            if (!redisConnected)
                return StatusCode(503, "Redis not connected");

            return Ok("Ready");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness check failed");
            return StatusCode(503, ex.Message);
        }
    }

    /// <summary>
    /// 存活检查
    /// </summary>
    [HttpGet("live")]
    public ActionResult Live()
    {
        return Ok("Live");
    }

    /// <summary>
    /// 交易子系统健康检查 — 返回闸门状态、账户资金、券商连接情况
    /// </summary>
    [HttpGet("trading")]
    public async Task<ActionResult<object>> GetTradingHealth()
    {
        var checks = new Dictionary<string, object>();
        var overallHealthy = true;

        // 1. 交易闸门
        checks["tradingGate"] = new
        {
            mode = _tradingGate.Mode.ToString(),
            halted = _tradingGate.IsHalted,
            todayOrderCount = _tradingGate.TodayOrderCount
        };
        if (_tradingGate.IsHalted) overallHealthy = false;

        // 2. 券商 Provider 连通性
        var brokerOk = false;
        string? brokerError = null;
        try
        {
            var provider = _dataProviderResolver.GetPrimaryProvider(Core.Enums.DataCapability.Trading);
            if (provider != null)
            {
                var account = await provider.GetAccountInfoAsync();
                brokerOk = true;
                checks["broker"] = new
                {
                    connected = true,
                    totalAssets = account.TotalAssets,
                    availableBalance = account.AvailableBalance,
                    positionCount = account.Positions.Count
                };
            }
            else
            {
                brokerError = "Trading provider not configured";
                checks["broker"] = new { connected = false, error = brokerError };
                overallHealthy = false;
            }
        }
        catch (Exception ex)
        {
            brokerError = ex.Message;
            checks["broker"] = new { connected = false, error = brokerError };
            overallHealthy = false;
            _logger.LogWarning("Trading health check: broker unreachable — {Error}", ex.Message);
        }

        // 3. 仓位汇总
        try
        {
            var summary = await _positionManager.GetPositionSummaryAsync();
            checks["portfolio"] = new
            {
                totalMarketValue = summary.PositionValue,
                positionCount = summary.Positions.Count,
                totalProfit = summary.TotalProfit,
                totalProfitRate = summary.TotalProfitRate
            };
        }
        catch (Exception ex)
        {
            checks["portfolio"] = new { error = ex.Message };
            _logger.LogWarning("Trading health check: position summary failed — {Error}", ex.Message);
        }

        var status = overallHealthy ? "Healthy" : "Degraded";
        var httpStatus = overallHealthy ? 200 : 503;

        return StatusCode(httpStatus, new
        {
            status,
            timestamp = DateTime.Now,
            checks
        });
    }
}
