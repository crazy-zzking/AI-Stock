using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Infrastructure.Database.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Controllers;

/// <summary>
/// 板块资金流 — 板块净流入/流出排行 + 板块内强势个股（资金流入/涨幅）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SectorController : ControllerBase
{
    private readonly IDataProviderResolver _resolver;
    private readonly AIStockDbContext _db;
    private readonly ILogger<SectorController> _logger;

    public SectorController(IDataProviderResolver resolver, AIStockDbContext db, ILogger<SectorController> logger)
    {
        _resolver = resolver;
        _db = db;
        _logger = logger;
    }

    // 板块能力当前由东财提供
    private EastmoneyProvider? Eastmoney()
        => _resolver.GetProviders(DataCapability.SectorRanking).FirstOrDefault() as EastmoneyProvider;

    /// <summary>板块资金流排行（实时，按主力净流入降序；前端可取头部=流入榜、尾部=流出榜）。</summary>
    [HttpGet("ranking")]
    public async Task<ActionResult<List<SectorFlowData>>> GetRanking(CancellationToken ct)
    {
        var em = Eastmoney();
        if (em == null) return Ok(new List<SectorFlowData>());
        var sectors = await em.GetSectorRankingAsync(ct);
        var ordered = sectors.OrderByDescending(s => s.NetInflow).ToList();
        return Ok(ordered);
    }

    /// <summary>
    /// 板块内强势个股：取板块成分股，join 最新快照按主力净流入降序，
    /// 返回资金流入 + 涨幅（默认仅看上涨的，按资金流入排）。
    /// </summary>
    [HttpGet("{sectorCode}/strong-stocks")]
    public async Task<ActionResult> GetStrongStocks(string sectorCode, [FromQuery] int top = 10, CancellationToken ct = default)
    {
        var em = Eastmoney();
        if (em == null) return Ok(Array.Empty<object>());

        var codes = await em.GetSectorConstituentsAsync(sectorCode, ct);
        if (codes.Count == 0) return Ok(Array.Empty<object>());

        var latestDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        if (latestDate == null) return Ok(Array.Empty<object>());

        var stocks = await _db.DailyMarketSnapshot
            .Where(s => s.Date == latestDate && codes.Contains(s.Code) && s.ChangePercent > 0)
            .OrderByDescending(s => s.MainNetInflow)
            .Take(top)
            .Select(s => new
            {
                code = s.Code,
                name = s.Name,
                changePercent = s.ChangePercent,
                mainNetInflow = s.MainNetInflow,
                turnoverRate = s.TurnoverRate,
                isLimitUp = s.IsLimitUp,
            })
            .ToListAsync(ct);

        return Ok(stocks);
    }
}
