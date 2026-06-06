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
    private readonly TailMarketBuyService _svc;
    public TailBuyController(TailMarketBuyService svc) => _svc = svc;

    /// <summary>手动触发一次尾盘选股 + 下单（默认 DryRun）。force=true 跳过 Enabled 开关，用于 DryRun 验证。</summary>
    [HttpPost("run")]
    public async Task<ActionResult> Run([FromQuery] bool force = true, CancellationToken ct = default)
    {
        var results = await _svc.RunAsync(ct, force);
        return Ok(results.Select(r => new { r.OrderId, r.Success, r.Status, r.Message }));
    }
}
