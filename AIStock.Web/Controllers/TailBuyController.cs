using AIStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 尾盘自动下单 — 手动触发接口（验证/应急用）。定时触发见 TailMarketBuyHostedService。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TailBuyController : ControllerBase
{
    private readonly TailMarketBuyService _buy;
    private readonly TailSellService _sell;
    public TailBuyController(TailMarketBuyService buy, TailSellService sell)
    {
        _buy = buy;
        _sell = sell;
    }

    /// <summary>手动触发一次尾盘卖出(到期/止损) + 买入(选股)。force=true 跳过 Enabled 开关，用于 DryRun 验证。</summary>
    [HttpPost("run")]
    public async Task<ActionResult> Run([FromQuery] bool force = true, CancellationToken ct = default)
    {
        var sold = await _sell.RunAsync(ct);
        var orders = await _buy.RunAsync(ct, force);
        return Ok(new { sold, orders = orders.Select(r => new { r.OrderId, r.Success, r.Status, r.Message }) });
    }
}
