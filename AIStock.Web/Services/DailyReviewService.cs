using System.Text;
using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Services;

/// <summary>
/// 每日复盘分析服务 — 基于已有数据源（每日快照 / 板块资金 / 龙虎榜 / 概念 / 事件 / 选股历史 / 订单），
/// 汇总当日领涨板块与个股、归因"为什么涨"、诊断数据完备性，生成复盘报告并落库（一交易日一份）。
/// </summary>
public class DailyReviewService
{
    private readonly AIStockDbContext _db;
    private readonly IDataProviderResolver _resolver;
    private readonly IOrderManager _orderManager;
    private readonly ITradingGate _tradingGate;
    private readonly ILogger<DailyReviewService> _logger;

    public DailyReviewService(
        AIStockDbContext db,
        IDataProviderResolver resolver,
        IOrderManager orderManager,
        ITradingGate tradingGate,
        ILogger<DailyReviewService> logger)
    {
        _db = db;
        _resolver = resolver;
        _orderManager = orderManager;
        _tradingGate = tradingGate;
        _logger = logger;
    }

    private EastmoneyProvider? Eastmoney()
        => _resolver.GetProviders(DataCapability.SectorRanking).FirstOrDefault() as EastmoneyProvider;

    /// <summary>取最近一次已落库的复盘；没有则按最新交易日生成一份。</summary>
    public async Task<DailyReviewReport?> GetOrCreateLatestAsync(CancellationToken ct = default)
    {
        var latest = await _db.DailyReview.OrderByDescending(r => r.TradingDate).FirstOrDefaultAsync(ct);
        if (latest != null)
            return Deserialize(latest.ReportJson);
        return await GenerateAndSaveAsync(null, ct);
    }

    /// <summary>取指定交易日的复盘（已落库才有）。</summary>
    public async Task<DailyReviewReport?> GetByDateAsync(DateTime date, CancellationToken ct = default)
    {
        var row = await _db.DailyReview.FirstOrDefaultAsync(r => r.TradingDate == date.Date, ct);
        return row == null ? null : Deserialize(row.ReportJson);
    }

    /// <summary>复盘历史列表（元信息，按交易日倒序）。</summary>
    public async Task<List<DailyReviewSummaryItem>> GetHistoryAsync(int take = 30, CancellationToken ct = default)
    {
        var rows = await _db.DailyReview
            .OrderByDescending(r => r.TradingDate)
            .Take(take <= 0 ? 30 : take)
            .Select(r => new DailyReviewSummaryItem
            {
                Id = r.Id,
                TradingDate = r.TradingDate,
                GeneratedAt = r.GeneratedAt,
                LimitUpCount = r.LimitUpCount,
                UpCount = r.UpCount,
                DownCount = r.DownCount,
            })
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>生成复盘并落库（按交易日 upsert，一天一份）。tradingDate 为空时取最新快照日。</summary>
    public async Task<DailyReviewReport?> GenerateAndSaveAsync(DateTime? tradingDate, CancellationToken ct = default)
    {
        var report = await GenerateAsync(tradingDate, ct);
        if (report == null) return null;

        var json = JsonSerializer.Serialize(report);
        var row = await _db.DailyReview.FirstOrDefaultAsync(r => r.TradingDate == report.TradingDate, ct);
        if (row == null)
        {
            _db.DailyReview.Add(new DailyReviewEntity
            {
                TradingDate = report.TradingDate,
                GeneratedAt = report.GeneratedAt,
                LimitUpCount = report.Market.LimitUpCount,
                UpCount = report.Market.UpCount,
                DownCount = report.Market.DownCount,
                ReportJson = json,
            });
        }
        else
        {
            row.GeneratedAt = report.GeneratedAt;
            row.LimitUpCount = report.Market.LimitUpCount;
            row.UpCount = report.Market.UpCount;
            row.DownCount = report.Market.DownCount;
            row.ReportJson = json;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("每日复盘已生成：{Date}，{Up}涨/{Down}跌/{LimitUp}涨停",
            report.TradingDate.ToString("yyyy-MM-dd"), report.Market.UpCount, report.Market.DownCount, report.Market.LimitUpCount);
        return report;
    }

    /// <summary>生成复盘报告（不落库）。</summary>
    public async Task<DailyReviewReport?> GenerateAsync(DateTime? tradingDate, CancellationToken ct = default)
    {
        // 市场分析以"最新快照交易日"为基准（无快照则取入参或今天）
        var snapDate = await _db.DailyMarketSnapshot.MaxAsync(s => (DateTime?)s.Date, ct);
        var date = (tradingDate?.Date) ?? snapDate?.Date ?? DateTime.Today;

        var report = new DailyReviewReport
        {
            TradingDate = date,
            GeneratedAt = DateTime.UtcNow,
        };

        var snaps = await _db.DailyMarketSnapshot.Where(s => s.Date == date).ToListAsync(ct);

        report.Orders = await BuildOrderReviewAsync(date);
        report.Market = BuildMarketBreadth(snaps);

        var hotThemes = await ComputeHotThemesAsync(snaps, ct);
        report.HotThemes = hotThemes;
        var hotConceptSet = hotThemes.Select(h => h.Concept).ToHashSet();

        report.TopStocks = await BuildTopStocksAsync(snaps, date, hotConceptSet, ct);
        report.TopSectors = await BuildTopSectorsAsync(ct);
        report.Selection = await BuildSelectionReviewAsync(snaps, ct);
        report.DataGaps = await BuildDataGapsAsync(snaps, date, report.TopSectors, ct);
        report.Summary = BuildSummary(report);

        return report;
    }

    // —— 交易复盘 ——
    private async Task<OrderReview> BuildOrderReviewAsync(DateTime date)
    {
        var review = new OrderReview { GateMode = _tradingGate.Mode.ToString() };
        try
        {
            var orders = await _orderManager.GetOrdersAsync(date, date.AddDays(1).AddSeconds(-1));
            review.TotalOrders = orders.Count;
            review.SuccessOrders = orders.Count(o => o.Status == OrderStatus.Filled || o.Status == OrderStatus.Submitted);
            review.FailedOrders = orders.Count(o => o.Status == OrderStatus.Failed);
            review.BuyOrders = orders.Count(o => string.Equals(o.Side, "buy", StringComparison.OrdinalIgnoreCase));
            review.SellOrders = orders.Count(o => string.Equals(o.Side, "sell", StringComparison.OrdinalIgnoreCase));
            review.TotalValue = orders.Sum(o => o.Price * o.Volume);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复盘：订单统计获取失败");
        }
        return review;
    }

    // —— 市场宽度 ——
    private static MarketBreadth BuildMarketBreadth(List<DailyMarketSnapshotEntity> snaps)
    {
        var m = new MarketBreadth
        {
            TotalCount = snaps.Count,
            UpCount = snaps.Count(s => s.ChangePercent > 0),
            DownCount = snaps.Count(s => s.ChangePercent < 0),
            FlatCount = snaps.Count(s => s.ChangePercent == 0),
            LimitUpCount = snaps.Count(s => s.IsLimitUp),
            TotalMainNetInflow = snaps.Sum(s => s.MainNetInflow),
        };
        m.AvgChangePercent = snaps.Count > 0 ? Math.Round(snaps.Average(s => s.ChangePercent), 2) : 0;
        return m;
    }

    // —— 当日热门题材：活跃股扎堆的概念 ——
    private async Task<List<HotThemeItem>> ComputeHotThemesAsync(List<DailyMarketSnapshotEntity> snaps, CancellationToken ct)
    {
        var activeCodes = snaps
            .Where(s => s.IsLimitUp || s.ChangePercent >= 5m || (s.VolumeRatio >= 2m && s.ChangePercent > 0))
            .Select(s => s.Code).ToList();
        if (activeCodes.Count == 0) return new();

        var rel = await _db.StockConceptRelation
            .Where(c => activeCodes.Contains(c.StockCode))
            .Select(c => new { c.StockCode, c.ConceptName })
            .ToListAsync(ct);

        var nameByCode = snaps.ToDictionary(s => s.Code, s => s.Name);
        var changeByCode = snaps.ToDictionary(s => s.Code, s => s.ChangePercent);

        return rel
            .GroupBy(c => c.ConceptName)
            .Select(g =>
            {
                var codes = g.Select(x => x.StockCode).Distinct().ToList();
                var leaders = codes
                    .OrderByDescending(c => changeByCode.TryGetValue(c, out var v) ? v : 0)
                    .Take(3)
                    .Select(c => nameByCode.TryGetValue(c, out var n) ? n : c)
                    .ToList();
                return new HotThemeItem { Concept = g.Key, ActiveStockCount = codes.Count, LeadingStocks = leaders };
            })
            .Where(x => x.ActiveStockCount >= 2)
            .OrderByDescending(x => x.ActiveStockCount)
            .Take(12)
            .ToList();
    }

    // —— 领涨个股 + 涨因归因 ——
    private async Task<List<StockReviewItem>> BuildTopStocksAsync(
        List<DailyMarketSnapshotEntity> snaps, DateTime date, HashSet<string> hotConcepts, CancellationToken ct)
    {
        var top = snaps.OrderByDescending(s => s.ChangePercent).Take(10).ToList();
        if (top.Count == 0) return new();
        var codes = top.Select(s => s.Code).ToList();

        var concepts = (await _db.StockConceptRelation
            .Where(c => codes.Contains(c.StockCode))
            .Select(c => new { c.StockCode, c.ConceptName })
            .ToListAsync(ct))
            .GroupBy(c => c.StockCode)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConceptName).Distinct().ToList());

        var dragons = (await _db.DragonTiger
            .Where(d => d.Date == date && codes.Contains(d.Code))
            .ToListAsync(ct))
            .GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.First());

        // 当日事件：related_stocks 含代码（粗匹配，DB 端 Contains）
        var dayStart = date; var dayEnd = date.AddDays(1);
        var events = await _db.EventRecord
            .Where(e => e.EventTime >= dayStart && e.EventTime < dayEnd && e.RelatedStocks != null)
            .Select(e => new { e.Title, e.RelatedStocks })
            .ToListAsync(ct);

        var items = new List<StockReviewItem>();
        foreach (var s in top)
        {
            var item = new StockReviewItem
            {
                Code = s.Code,
                Name = s.Name,
                ChangePercent = s.ChangePercent,
                MainNetInflow = s.MainNetInflow,
                TurnoverRate = s.TurnoverRate,
                IsLimitUp = s.IsLimitUp,
            };
            if (concepts.TryGetValue(s.Code, out var cs))
            {
                item.Concepts = cs;
                item.HotConcepts = cs.Where(hotConcepts.Contains).ToList();
            }
            if (dragons.TryGetValue(s.Code, out var d))
            {
                item.OnDragonTiger = true;
                item.DragonTigerReason = d.Reason;
            }
            item.Events = events
                .Where(e => e.RelatedStocks!.Contains(s.Code))
                .Select(e => e.Title).Take(3).ToList();

            item.Reason = BuildStockReason(item);
            items.Add(item);
        }
        return items;
    }

    private static string BuildStockReason(StockReviewItem s)
    {
        var parts = new List<string>();
        if (s.IsLimitUp) parts.Add("涨停");
        if (s.MainNetInflow > 0) parts.Add($"主力净流入 {FmtYi(s.MainNetInflow)}");
        else if (s.MainNetInflow < 0) parts.Add($"主力净流出 {FmtYi(Math.Abs(s.MainNetInflow))}（拉升但资金流出，谨慎）");
        if (s.HotConcepts.Count > 0) parts.Add($"踩中热门题材 {string.Join("、", s.HotConcepts.Take(2))}");
        else if (s.Concepts.Count > 0) parts.Add($"概念：{string.Join("、", s.Concepts.Take(2))}");
        if (s.OnDragonTiger) parts.Add($"上龙虎榜（{Trim(s.DragonTigerReason, 20)}）");
        if (s.Events.Count > 0) parts.Add($"消息：{Trim(s.Events[0], 24)}");
        if (s.TurnoverRate >= 15) parts.Add($"换手 {s.TurnoverRate:F1}%（高活跃）");
        return parts.Count > 0 ? string.Join("；", parts) : "无明显资金/题材/消息支撑，注意持续性";
    }

    // —— 领涨板块 ——（板块资金为实时接口，按涨幅排序取前列）
    private async Task<List<SectorReviewItem>> BuildTopSectorsAsync(CancellationToken ct)
    {
        var em = Eastmoney();
        if (em == null) return new();
        List<SectorFlowData> sectors;
        try
        {
            sectors = await em.GetSectorRankingAsync(false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复盘：板块数据获取失败");
            return new();
        }

        var top = sectors.OrderByDescending(s => s.ChangePercent).Take(6).ToList();
        var result = new List<SectorReviewItem>();
        foreach (var s in top)
        {
            var item = new SectorReviewItem
            {
                SectorCode = s.SectorCode,
                SectorName = s.SectorName,
                ChangePercent = s.ChangePercent,
                NetInflow = s.NetInflow,
            };
            try
            {
                var flows = await em.GetSectorStockFlowAsync(s.SectorCode, 3, ct);
                item.LeadingStocks = flows.Select(f => f.Name).ToList();
            }
            catch { /* EM 不可达则留空 */ }

            item.Reason = s.NetInflow > 0
                ? $"主力净流入 {FmtYi(s.NetInflow)}，板块走强"
                : "涨幅居前但主力未净流入，留意持续性";
            result.Add(item);
        }
        return result;
    }

    // —— 选股回测：最近一次选股在当日的表现 ——
    private async Task<SelectionReview?> BuildSelectionReviewAsync(List<DailyMarketSnapshotEntity> snaps, CancellationToken ct)
    {
        var latest = await _db.SelectionResult.OrderByDescending(r => r.RunAt).FirstOrDefaultAsync(ct);
        if (latest == null) return null;

        List<StockSelectionResult> picks;
        try { picks = JsonSerializer.Deserialize<List<StockSelectionResult>>(latest.ResultsJson) ?? new(); }
        catch { return null; }
        if (picks.Count == 0) return null;

        var changeByCode = snaps.ToDictionary(s => s.Code, s => s.ChangePercent);
        var items = picks.Select(p =>
        {
            decimal? chg = changeByCode.TryGetValue(p.Code, out var v) ? v : (decimal?)null;
            return new SelectionReviewItem
            {
                Code = p.Code,
                Name = p.Name,
                ChangePercent = chg,
                Hit = chg.HasValue && chg.Value > 0,
            };
        }).ToList();

        var withData = items.Where(i => i.ChangePercent.HasValue).ToList();
        return new SelectionReview
        {
            SelectionRunAt = latest.RunAt,
            Count = items.Count,
            HitCount = items.Count(i => i.Hit),
            AvgChangePercent = withData.Count > 0 ? Math.Round(withData.Average(i => i.ChangePercent!.Value), 2) : 0,
            Items = items,
        };
    }

    // —— 数据完备性诊断 ——
    private async Task<List<DataGapItem>> BuildDataGapsAsync(
        List<DailyMarketSnapshotEntity> snaps, DateTime date, List<SectorReviewItem> sectors, CancellationToken ct)
    {
        var gaps = new List<DataGapItem>();

        gaps.Add(new DataGapItem
        {
            Source = "daily_market_snapshot（行情/资金/技术指标）",
            Available = snaps.Count > 0,
            Note = snaps.Count > 0 ? $"{snaps.Count} 只" : "当日无快照，涨跌家数/领涨个股/资金归因均缺失，请先跑 market-snapshot 采集",
        });

        var dragonCount = await _db.DragonTiger.CountAsync(d => d.Date == date, ct);
        gaps.Add(new DataGapItem
        {
            Source = "dragon_tiger（龙虎榜）",
            Available = dragonCount > 0,
            Note = dragonCount > 0 ? $"{dragonCount} 只上榜" : "当日无龙虎榜，游资/机构席位归因缺失，请跑 dragon-tiger 采集",
        });

        var conceptCount = await _db.StockConceptRelation.CountAsync(ct);
        gaps.Add(new DataGapItem
        {
            Source = "stock_concept_relation（概念/题材）",
            Available = conceptCount > 0,
            Note = conceptCount > 0 ? $"{conceptCount} 条关联" : "无概念数据，热门题材/题材归因缺失，请跑股票明细同步",
        });

        var dayStart = date; var dayEnd = date.AddDays(1);
        var eventCount = await _db.EventRecord.CountAsync(e => e.EventTime >= dayStart && e.EventTime < dayEnd, ct);
        gaps.Add(new DataGapItem
        {
            Source = "event_record（消息/事件）",
            Available = eventCount > 0,
            Note = eventCount > 0 ? $"{eventCount} 条当日事件" : "当日无事件，消息面归因缺失（情报/研报采集未跑或当日无新增）",
        });

        gaps.Add(new DataGapItem
        {
            Source = "板块资金（东财实时接口）",
            Available = sectors.Count > 0,
            Note = sectors.Count > 0 ? "可用" : "东财接口不可达（隧道代理/网络），领涨板块缺失",
        });

        return gaps;
    }

    // —— 文字总结 ——
    private static string BuildSummary(DailyReviewReport r)
    {
        var sb = new StringBuilder();
        var m = r.Market;
        sb.Append($"{r.TradingDate:yyyy-MM-dd} 复盘：");
        if (m.TotalCount > 0)
            sb.Append($"全市场 {m.UpCount} 涨 / {m.DownCount} 跌，{m.LimitUpCount} 只涨停，平均涨幅 {m.AvgChangePercent:+0.00;-0.00}%，主力合计{(m.TotalMainNetInflow >= 0 ? "净流入" : "净流出")} {FmtYi(Math.Abs(m.TotalMainNetInflow))}。");
        else
            sb.Append("当日无市场快照数据。");

        if (r.TopSectors.Count > 0)
            sb.Append($" 领涨板块：{string.Join("、", r.TopSectors.Take(3).Select(s => $"{s.SectorName}({s.ChangePercent:+0.0;-0.0}%)"))}。");
        if (r.HotThemes.Count > 0)
            sb.Append($" 当下风口题材：{string.Join("、", r.HotThemes.Take(3).Select(h => $"{h.Concept}({h.ActiveStockCount}只)"))}。");
        if (r.TopStocks.Count > 0)
        {
            var lead = r.TopStocks[0];
            sb.Append($" 最强个股 {lead.Name}({lead.ChangePercent:+0.0;-0.0}%)：{lead.Reason}。");
        }
        if (r.Selection != null && r.Selection.Count > 0)
            sb.Append($" 选股回测：{r.Selection.Count} 只命中 {r.Selection.HitCount} 只上涨，平均 {r.Selection.AvgChangePercent:+0.00;-0.00}%。");

        var missing = r.DataGaps.Where(g => !g.Available).Select(g => g.Source).ToList();
        if (missing.Count > 0)
            sb.Append($" ⚠ 数据缺口：缺少 {string.Join("、", missing)}，以上归因可能不完整。");
        else
            sb.Append(" 数据完备。");

        return sb.ToString();
    }

    private static DailyReviewReport? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<DailyReviewReport>(json); }
        catch { return null; }
    }

    private static string FmtYi(decimal v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1e8m) return $"{v / 1e8m:F2}亿";
        if (abs >= 1e4m) return $"{v / 1e4m:F0}万";
        return $"{v:F0}";
    }

    private static string Trim(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s[..max] + "…" : s);
}
