using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 交易执行API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ExecutionController : ControllerBase
{
    private readonly IOrderManager _orderManager;
    private readonly IPositionManager _positionManager;
    private readonly ILogger<ExecutionController> _logger;

    public ExecutionController(
        IOrderManager orderManager,
        IPositionManager positionManager,
        ILogger<ExecutionController> logger)
    {
        _orderManager = orderManager;
        _positionManager = positionManager;
        _logger = logger;
    }

    /// <summary>
    /// 下单
    /// </summary>
    [HttpPost("order")]
    public async Task<ActionResult<OrderResult>> PlaceOrder([FromBody] OrderRequest request)
    {
        try
        {
            var result = await _orderManager.PlaceOrderAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order");
            return StatusCode(500, "Failed to place order");
        }
    }

    /// <summary>
    /// 撤单
    /// </summary>
    [HttpDelete("order/{orderId}")]
    public async Task<ActionResult<bool>> CancelOrder(string orderId)
    {
        try
        {
            var result = await _orderManager.CancelOrderAsync(orderId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {OrderId}", orderId);
            return StatusCode(500, "Failed to cancel order");
        }
    }

    /// <summary>
    /// 查询订单状态
    /// </summary>
    [HttpGet("order/{orderId}/status")]
    public async Task<ActionResult<OrderStatus>> GetOrderStatus(string orderId)
    {
        var status = await _orderManager.GetOrderStatusAsync(orderId);
        return Ok(status);
    }

    /// <summary>
    /// 获取订单列表
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<List<OrderInfo>>> GetOrders(
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var orders = await _orderManager.GetOrdersAsync(startTime, endTime);
        return Ok(orders);
    }

    /// <summary>
    /// 获取所有持仓
    /// </summary>
    [HttpGet("positions")]
    public async Task<ActionResult<List<PortfolioPosition>>> GetPositions()
    {
        var positions = await _positionManager.GetPositionsAsync();
        return Ok(positions);
    }

    /// <summary>
    /// 获取指定股票持仓
    /// </summary>
    [HttpGet("positions/{code}")]
    public async Task<ActionResult<PortfolioPosition>> GetPosition(string code)
    {
        var position = await _positionManager.GetPositionAsync(code);
        if (position == null)
        {
            return NotFound($"No position found for {code}");
        }
        return Ok(position);
    }

    /// <summary>
    /// 获取持仓汇总
    /// </summary>
    [HttpGet("positions/summary")]
    public async Task<ActionResult<PositionSummary>> GetPositionSummary()
    {
        var summary = await _positionManager.GetSummaryAsync();
        return Ok(summary);
    }

    /// <summary>
    /// 更新持仓
    /// </summary>
    [HttpPut("positions")]
    public async Task<ActionResult> UpdatePosition([FromBody] PortfolioPosition position)
    {
        await _positionManager.UpdatePositionAsync(position);
        return Ok();
    }

    /// <summary>
    /// 删除持仓
    /// </summary>
    [HttpDelete("positions/{code}")]
    public async Task<ActionResult> RemovePosition(string code)
    {
        await _positionManager.RemovePositionAsync(code);
        return Ok();
    }
}
