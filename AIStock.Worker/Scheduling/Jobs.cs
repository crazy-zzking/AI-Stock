using AIStock.Core.Interfaces;
using AIStock.Worker.Services;
using Microsoft.Extensions.Options;

namespace AIStock.Worker.Scheduling;

// 数据类任务 ---------------------------------------------------------------

/// <summary>股票池同步任务</summary>
public class StockBaseSyncJob : IScheduledJob
{
    private readonly DataSyncService _sync;
    public StockBaseSyncJob(DataSyncService sync) => _sync = sync;
    public string Name => "stock-base";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncStockBaseAsync(ct);
}

/// <summary>股票明细（行业/概念）同步任务</summary>
public class StockDetailSyncJob : IScheduledJob
{
    private readonly DataSyncService _sync;
    public StockDetailSyncJob(DataSyncService sync) => _sync = sync;
    public string Name => "stock-detail";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncStockDetailsAsync(ct);
}

/// <summary>日K同步任务（收盘后/启动补拉，内部按交易日与库内最新日期判断）</summary>
public class KlineSyncJob : IScheduledJob
{
    private readonly DataSyncService _sync;
    private readonly DataSyncOptions _options;
    private readonly ILogger<KlineSyncJob> _logger;

    public KlineSyncJob(DataSyncService sync, IOptions<DataSyncOptions> options, ILogger<KlineSyncJob> logger)
    {
        _sync = sync;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "kline";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var (need, last, target) = await _sync.ShouldSyncKlinesAsync(_options.SyncHour, ct);
        if (!need)
        {
            _logger.LogInformation("K线已最新（截至 {Last}），跳过", last?.ToString("yyyy-MM-dd"));
            return;
        }
        _logger.LogInformation("K线需同步：库内最新 {Last}，目标交易日 {Target}",
            last?.ToString("yyyy-MM-dd") ?? "无", target.ToString("yyyy-MM-dd"));
        await _sync.SyncKlinesAsync(ct);
    }
}

// 情报类任务 ---------------------------------------------------------------

/// <summary>财经新闻采集任务</summary>
public class NewsCollectJob : IScheduledJob
{
    private readonly IntelligenceSyncService _sync;
    public NewsCollectJob(IntelligenceSyncService sync) => _sync = sync;
    public string Name => "news";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncNewsAsync(ct);
}

/// <summary>公告采集任务（标题关键字过滤后送 LLM）</summary>
public class AnnouncementCollectJob : IScheduledJob
{
    private readonly IntelligenceSyncService _sync;
    public AnnouncementCollectJob(IntelligenceSyncService sync) => _sync = sync;
    public string Name => "announcement";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncAnnouncementsAsync(ct);
}

/// <summary>研报采集任务</summary>
public class ReportCollectJob : IScheduledJob
{
    private readonly IntelligenceSyncService _sync;
    public ReportCollectJob(IntelligenceSyncService sync) => _sync = sync;
    public string Name => "report";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncReportsAsync(ct);
}

/// <summary>知识星球采集任务（盘中密集、盘后稀疏，随机间隔防封）</summary>
public class KnowledgeStarCollectJob : IScheduledJob
{
    private readonly IntelligenceSyncService _sync;
    private readonly ITradingCalendar _calendar;
    public KnowledgeStarCollectJob(IntelligenceSyncService sync, ITradingCalendar calendar)
    {
        _sync = sync;
        _calendar = calendar;
    }
    public string Name => "knowledge-star";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncKnowledgeStarAsync(ct);

    public bool HasDynamicSchedule => true;

    /// <summary>交易日盘中(9:30-15:00) 5-10 分钟随机；其余(收盘/周末/节假日) 1-2 小时随机。</summary>
    public async Task<TimeSpan?> GetNextDelayAsync(CancellationToken ct)
    {
        var now = DateTime.Now; // 本地即北京时间
        var isTradingDay = await _calendar.IsTradingDayAsync(now.Date, ct);
        if (isTradingDay && PositionCacheService.IsInTradingHours(now))
            return TimeSpan.FromMinutes(5 + Random.Shared.NextDouble() * 5);  // 5-10 分钟
        return TimeSpan.FromMinutes(60 + Random.Shared.NextDouble() * 60);    // 1-2 小时
    }
}

/// <summary>候选边晋升任务（达到阈值的候选边转入权威图谱）</summary>
public class GraphPromoteJob : IScheduledJob
{
    private readonly AIStock.EventEngine.Services.GraphPromotionService _svc;
    public GraphPromoteJob(AIStock.EventEngine.Services.GraphPromotionService svc) => _svc = svc;
    public string Name => "graph-promote";
    public Task ExecuteAsync(CancellationToken ct) => _svc.PromoteAsync(ct);
}

/// <summary>持仓缓存刷新任务 — 定时从 Sanhu 拉取持仓写入 Redis</summary>
public class PositionCacheJob : IScheduledJob
{
    private readonly PositionCacheService _svc;
    public PositionCacheJob(PositionCacheService svc) => _svc = svc;
    public string Name => "position-cache";
    public Task ExecuteAsync(CancellationToken ct) => _svc.RefreshCacheAsync(ct);
}

/// <summary>
/// 市场快照采集任务。启用盘中(EnableIntraday)时：交易时段/收盘窗口用腾讯批量周期刷新快照(支持盘中选股)，
/// 动态间隔；非交易时段跳过。未启用时：沿用收盘后全量(按配置 DailyAtHour)。
/// </summary>
public class MarketSnapshotSyncJob : IScheduledJob
{
    private readonly MarketSnapshotSyncService _svc;
    private readonly ITradingCalendar _calendar;
    private readonly MarketSnapshotOptions _options;
    private readonly ILogger<MarketSnapshotSyncJob> _logger;

    public MarketSnapshotSyncJob(MarketSnapshotSyncService svc, ITradingCalendar calendar,
        IOptions<MarketSnapshotOptions> options, ILogger<MarketSnapshotSyncJob> logger)
    {
        _svc = svc;
        _calendar = calendar;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "market-snapshot";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.EnableIntraday)
        {
            await _svc.SyncAsync(ct); // 未启用盘中：收盘后全量
            return;
        }

        var now = DateTime.Now;
        if (!await _calendar.IsTradingDayAsync(now.Date, ct)) { _logger.LogDebug("非交易日，跳过快照"); return; }
        if (!InCollectWindow(now)) { _logger.LogDebug("非采集时段，跳过快照"); return; }

        await _svc.SyncIntradayAsync(ct); // 盘中/收盘窗口：腾讯批量
    }

    public bool HasDynamicSchedule => _options.EnableIntraday;

    public async Task<TimeSpan?> GetNextDelayAsync(CancellationToken ct)
    {
        if (!_options.EnableIntraday) return null; // 走配置(DailyAtHour/IntervalSeconds)
        var now = DateTime.Now;
        var isTradingDay = await _calendar.IsTradingDayAsync(now.Date, ct);
        if (isTradingDay && InCollectWindow(now))
            return TimeSpan.FromMinutes(Math.Max(1, _options.IntradayIntervalMinutes));
        if (isTradingDay && now.TimeOfDay < new TimeSpan(9, 30, 0))
            return new TimeSpan(9, 30, 0) - now.TimeOfDay; // 睡到开盘
        return TimeSpan.FromMinutes(30); // 其它时段 30 分钟复评（ExecuteAsync 会快速跳过）
    }

    /// <summary>采集窗口：盘中 9:30-11:30 / 13:00-15:00，外加收盘补采 15:00-16:00。</summary>
    private static bool InCollectWindow(DateTime now)
    {
        var t = now.TimeOfDay;
        return (t >= new TimeSpan(9, 30, 0) && t <= new TimeSpan(11, 30, 0))
            || (t >= new TimeSpan(13, 0, 0) && t <= new TimeSpan(16, 0, 0));
    }
}

/// <summary>龙虎榜采集任务（收盘后）</summary>
public class DragonTigerCollectJob : IScheduledJob
{
    private readonly DragonTigerSyncService _svc;
    public DragonTigerCollectJob(DragonTigerSyncService svc) => _svc = svc;
    public string Name => "dragon-tiger";
    public Task ExecuteAsync(CancellationToken ct) => _svc.SyncAsync(null, ct); // 自动取最近交易日
}
