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
    private readonly ILogger<HealthController> _logger;

    public HealthController(IConnectionMultiplexer redis, ILogger<HealthController> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
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
            var serverTime = DateTime.UtcNow;

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
}
