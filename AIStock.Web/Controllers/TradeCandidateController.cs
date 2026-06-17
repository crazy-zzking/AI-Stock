using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Controllers;

/// <summary>
/// 交易候选池 API。数据由 Worker 每日选股 + LLM 复评后自动写入 trade_candidate（建议买入的票，
/// 含 AI 推荐理由 + 买入价/止损/止盈）。此处供前端展示并人工一键下单（走 OrderManager 全部安全闸门）。
/// </summary>
[ApiController]
[Route("api/trade-candidate")]
public class TradeCandidateController : ControllerBase
{
    private readonly AIStockDbContext _db;
    private readonly IOrderManager _orderManager;
    private readonly ITradingGate _tradingGate;
    private readonly ILogger<TradeCandidateController> _logger;

    public TradeCandidateController(
        AIStockDbContext db, IOrderManager orderManager, ITradingGate tradingGate,
        ILogger<TradeCandidateController> logger)
    {
        _db = db;
        _orderManager = orderManager;
        _tradingGate = tradingGate;
        _logger = logger;
    }

    /// <summary>
    /// 候选池列表。默认返回最近 N 天、待处理(status=0)的候选；可按日期/状态/策略过滤。
    /// status：-1=全部，0=待处理，1=已下单，2=已忽略。
    /// </summary>
    [HttpGet("")]
    public async Task<ActionResult> List(
        [FromQuery] int days = 7, [FromQuery] int status = 0,
        [FromQuery] string? strategy = null, [FromQuery] string? date = null,
        CancellationToken ct = default)
    {
        if (days <= 0 || days > 90) days = 7;
        var q = _db.TradeCandidate.AsQueryable();

        if (DateTime.TryParse(date, out var d))
            q = q.Where(c => c.TradingDate == d.Date);
        else
            q = q.Where(c => c.TradingDate >= DateTime.Today.AddDays(-days));

        if (status >= 0) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrEmpty(strategy)) q = q.Where(c => c.TopStrategy == strategy);

        var rows = await q
            .OrderByDescending(c => c.TradingDate)
            .ThenByDescending(c => c.Score)
            .Take(500)
            .ToListAsync(ct);

        return Ok(new
        {
            mode = _tradingGate.Mode.ToString(),
            halted = _tradingGate.IsHalted,
            items = rows.Select(ToDto),
        });
    }

    /// <summary>对某候选下买单。price 不传则用买入价上沿；volume 必填（股，须为100整数倍）。
    /// 若之前已下单但被券商拒绝，允许重新下单。</summary>
    [HttpPost("{id:long}/order")]
    public async Task<ActionResult> Order(long id, [FromBody] CandidateOrderRequest req, CancellationToken ct = default)
    {
        var c = await _db.TradeCandidate.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound($"候选 {id} 不存在");

        // 如果已有订单号，先查单确认状态；上一单失败（被拒/已撤单/查不到）则重置以便重新下单
        if (c.Status == 1 && !string.IsNullOrEmpty(c.OrderId))
        {
            var lastDetail = await _orderManager.GetOrderDetailAsync(c.OrderId);
            if (lastDetail == null || !lastDetail.Success)
                ResetCandidateForRetry(c, lastDetail, lastDetail?.OrderStatusText);
        }

        if (c.Status == 1) return BadRequest("该候选已下单（订单仍在处理中），请先通过 check-order 确认状态");

        var price = req.Price is > 0 ? req.Price.Value : (c.BuyHigh > 0 ? c.BuyHigh : c.RefClose);
        if (price <= 0) return BadRequest("无有效价格，请显式传 price");
        var volume = req.Volume;
        if (volume <= 0 || volume % 100 != 0) return BadRequest("volume 必须为 100 的整数倍且大于 0");

        var order = new OrderRequest
        {
            Code = c.Code,
            Side = "buy",
            OrderType = OrderType.Limit,
            Price = price,
            Volume = volume,
            StrategyName = $"{c.TopStrategy}-candidate",
            SignalId = $"candidate-{c.Id}",
        };

        var res = await _orderManager.PlaceOrderAsync(order);
        _logger.LogInformation("候选池下单 {Code} {Name} 量={Vol}@{Price} → success={OK} status={Status} msg={Msg} brokerRejected={Br}",
            c.Code, c.Name, volume, price, res.Success, res.Status, res.Message, res.IsBrokerRejected);

        if (res.Success)
        {
            c.Status = 1;
            c.OrderId = res.OrderId;
            c.OrderPrice = price;
            c.OrderVolume = volume;
            await _db.SaveChangesAsync(ct);
        }
        else if (res.IsBrokerRejected)
        {
            // 券商拒绝：仍记录 orderId 供后续查询，但状态保持 0 以便重试
            c.OrderId = res.OrderId;
            c.OrderPrice = price;
            c.OrderVolume = volume;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            success = res.Success,
            status = res.Status.ToString(),
            message = res.Message,
            brokerTip = res.BrokerTip,
            brokerRejected = res.IsBrokerRejected,
            orderId = res.OrderId,
        });
    }

    /// <summary>
    /// 查询候选已下订单的状态（调用 jycx_chadan）。
    /// 若券商已拒绝该订单，自动将候选重置为可重新下单（status=0）。
    /// </summary>
    [HttpPost("{id:long}/check-order")]
    public async Task<ActionResult> CheckOrder(long id, CancellationToken ct = default)
    {
        var c = await _db.TradeCandidate.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound($"候选 {id} 不存在");
        if (string.IsNullOrEmpty(c.OrderId)) return BadRequest("该候选尚未下单，无订单可查");

        var detail = await _orderManager.GetOrderDetailAsync(c.OrderId);

        if (detail == null)
        {
            return Ok(new
            {
                found = false,
                orderId = c.OrderId,
                message = "订单查询失败（订单可能已过期或Provider不可用）",
            });
        }

        // 若订单失败（券商拒绝/已撤单/状态错误等），重置候选状态以允许重新下单
        if (!detail.Success && c.Status == 1)
        {
            var reason = detail.IsBrokerRejected
                ? $"券商拒绝：{detail.BrokerTip ?? detail.OrderStatusText}"
                : detail.OrderStatusText;
            ResetCandidateForRetry(c, detail, reason);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // 缓存最新查单结果
            c.OrderStatusText = detail.OrderStatusText;
            c.OrderFilledVolume = detail.FilledVolume;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            found = true,
            orderId = detail.OrderId,
            status = detail.Status.ToString(),
            success = detail.Success,
            message = detail.Message,
            brokerTip = detail.BrokerTip,
            brokerRejected = detail.IsBrokerRejected,
            candidateStatus = detail.Success ? c.Status : 0,
            filledVolume = detail.FilledVolume,
            orderStatusText = detail.OrderStatusText,
        });
    }

    /// <summary>忽略某候选（status=2），从待处理列表移除。</summary>
    [HttpPost("{id:long}/ignore")]
    public async Task<ActionResult> Ignore(long id, CancellationToken ct = default)
    {
        var c = await _db.TradeCandidate.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound($"候选 {id} 不存在");
        if (c.Status == 1) return BadRequest("已下单的候选不可忽略");
        c.Status = 2;
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    /// <summary>把候选重置为「待处理」(status=0) 以便重新下单：上一单失败/被拒/查不到时调用。
    /// 不在此处 SaveChanges，由调用方决定何时落库。</summary>
    private void ResetCandidateForRetry(TradeCandidateEntity c, OrderResult? detail, string? statusText)
    {
        _logger.LogInformation("候选 {Id} 上次订单 {OrderId} 失败（{Status}）→ 重置为待处理",
            c.Id, c.OrderId, statusText ?? detail?.Status.ToString());
        c.Status = 0;
        c.OrderId = null;
        c.OrderPrice = null;
        c.OrderVolume = null;
        c.OrderStatusText = statusText ?? detail?.OrderStatusText;
        c.OrderFilledVolume = detail?.FilledVolume ?? 0;
    }

    private static object ToDto(TradeCandidateEntity c)
    {
        List<string> tags;
        try { tags = JsonSerializer.Deserialize<List<string>>(c.Tags) ?? new(); }
        catch { tags = new(); }
        List<string> hitStrategies;
        try { hitStrategies = JsonSerializer.Deserialize<List<string>>(c.HitStrategies) ?? new(); }
        catch { hitStrategies = new(); }

        decimal rr = 0;
        var risk = c.RefClose - c.StopLoss;
        var reward = c.TakeProfit - c.RefClose;
        if (risk > 0) rr = Math.Round(reward / risk, 1);

        return new
        {
            id = c.Id,
            tradingDate = c.TradingDate,
            code = c.Code,
            name = c.Name,
            score = c.Score,
            ratingStars = c.RatingStars,
            tags,
            topStrategy = c.TopStrategy,
            topStrategyName = c.TopStrategyName,
            hitStrategies,
            hitCount = c.HitCount,
            narrative = c.Narrative,
            refClose = c.RefClose,
            buyLow = c.BuyLow,
            buyHigh = c.BuyHigh,
            stopLoss = c.StopLoss,
            takeProfit = c.TakeProfit,
            riskReward = rr,
            planBasis = c.PlanBasis,
            status = c.Status,
            orderId = c.OrderId,
            orderPrice = c.OrderPrice,
            orderVolume = c.OrderVolume,
            orderStatusText = c.OrderStatusText,
            orderFilledVolume = c.OrderFilledVolume,
        };
    }
}

/// <summary>候选池下单请求体</summary>
public class CandidateOrderRequest
{
    /// <summary>下单价（元）；不传用买入价上沿</summary>
    public decimal? Price { get; set; }
    /// <summary>下单量（股，100 整数倍）</summary>
    public long Volume { get; set; }
}
