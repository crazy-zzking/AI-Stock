using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Data.Providers.Eastmoney;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AIStock.Web.Controllers;

/// <summary>
/// 板块资金流 — 板块净流入/流出排行 + 板块内强势个股（资金流入/涨幅）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SectorController : ControllerBase
{
    private readonly IDataProviderResolver _resolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SectorController> _logger;

    public SectorController(IDataProviderResolver resolver, IMemoryCache cache, ILogger<SectorController> logger)
    {
        _resolver = resolver;
        _cache = cache;
        _logger = logger;
    }

    // 板块能力当前由东财提供
    private EastmoneyProvider? Eastmoney()
        => _resolver.GetProviders(DataCapability.SectorRanking).FirstOrDefault() as EastmoneyProvider;

    /// <summary>
    /// 板块资金流排行。direction=inflow：主力净流入降序（流入榜）；direction=outflow：弱势/流出板块（独立查询）。
    /// 保留东财服务端排序。盘中缓存 60 秒、休市 10 分钟，减少对东财的实时请求。
    /// </summary>
    [HttpGet("ranking")]
    public async Task<ActionResult<List<SectorFlowData>>> GetRanking([FromQuery] string direction = "inflow", CancellationToken ct = default)
    {
        var outflow = string.Equals(direction, "outflow", StringComparison.OrdinalIgnoreCase);
        var key = $"sector:ranking:{(outflow ? "outflow" : "inflow")}";
        if (_cache.TryGetValue(key, out List<SectorFlowData>? cached) && cached != null)
            return Ok(cached);

        var em = Eastmoney();
        if (em == null) return Ok(new List<SectorFlowData>());
        var sectors = await em.GetSectorRankingAsync(outflow, ct);

        if (sectors.Count > 0) _cache.Set(key, sectors, CacheTtl());
        return Ok(sectors);
    }

    /// <summary>缓存时长：交易日盘中 60 秒，其余 10 分钟。</summary>
    private static TimeSpan CacheTtl()
    {
        var now = DateTime.Now; // 本地即北京时间
        var t = now.TimeOfDay;
        var inTrading = now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday
            && ((t >= new TimeSpan(9, 30, 0) && t <= new TimeSpan(11, 30, 0))
                || (t >= new TimeSpan(13, 0, 0) && t <= new TimeSpan(15, 0, 0)));
        return inTrading ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// 板块内强势个股：实时取板块内个股资金流（东财服务端按主力净流入降序），
    /// 返回资金流入 + 涨幅 + 主力净占比。不依赖快照表，盘中即时反映。
    /// </summary>
    [HttpGet("{sectorCode}/strong-stocks")]
    public async Task<ActionResult> GetStrongStocks(string sectorCode, [FromQuery] int top = 10, CancellationToken ct = default)
    {
        var key = $"sector:strong:{sectorCode}:{top}";
        if (_cache.TryGetValue(key, out object? cachedStocks) && cachedStocks != null)
            return Ok(cachedStocks);

        var em = Eastmoney();
        if (em == null) return Ok(Array.Empty<object>());

        var flows = await em.GetSectorStockFlowAsync(sectorCode, top, ct);
        var stocks = flows.Select(s => new
        {
            code = s.Code,
            name = s.Name,
            price = s.Price,
            changePercent = s.ChangePercent,
            mainNetInflow = s.MainNetInflow,
            mainNetRatio = s.MainNetRatio,
            isLimitUp = s.ChangePercent >= 9.8m,
        }).ToList();

        if (stocks.Count > 0) _cache.Set(key, stocks, CacheTtl());
        return Ok(stocks);
    }
}
