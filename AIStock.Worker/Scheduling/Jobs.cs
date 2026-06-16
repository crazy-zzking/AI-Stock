using AIStock.Core.Interfaces;
using AIStock.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IWorkerConfigProvider _config;
    private readonly ILogger<KlineSyncJob> _logger;

    public KlineSyncJob(DataSyncService sync, IWorkerConfigProvider config, ILogger<KlineSyncJob> logger)
    {
        _sync = sync;
        _config = config;
        _logger = logger;
    }

    public string Name => "kline";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = await _config.GetAsync<DataSyncOptions>(DataSyncOptions.SectionName, ct);
        var (need, last, target) = await _sync.ShouldSyncKlinesAsync(opt.SyncHour, ct);
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
    private readonly IWorkerConfigProvider _config;
    private MarketSnapshotOptions _options = new();
    private readonly ILogger<MarketSnapshotSyncJob> _logger;

    public MarketSnapshotSyncJob(MarketSnapshotSyncService svc, ITradingCalendar calendar,
        IWorkerConfigProvider config, ILogger<MarketSnapshotSyncJob> logger)
    {
        _svc = svc;
        _calendar = calendar;
        _config = config;
        _logger = logger;
    }

    public string Name => "market-snapshot";

    private async Task RefreshOptionsAsync(CancellationToken ct)
        => _options = await _config.GetAsync<MarketSnapshotOptions>(MarketSnapshotOptions.SectionName, ct);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await RefreshOptionsAsync(ct);
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
        await RefreshOptionsAsync(ct);
        if (!_options.EnableIntraday) return null; // 走配置(DailyAtHour/IntervalSeconds)
        var now = DateTime.Now;
        var isTradingDay = await _calendar.IsTradingDayAsync(now.Date, ct);
        if (isTradingDay && InCollectWindow(now))
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntradayIntervalMinutes));
            // 尾盘决策点对齐：下一次常规刷新若会跨过 CloseDecisionTime，则提前对齐到该时点，
            // 保证尾盘(如 14:55)选股/下单用到当时最新快照，而非上一次的旧快照。
            if (TimeSpan.TryParse(_options.CloseDecisionTime, out var decisionT))
            {
                var nowT = now.TimeOfDay;
                if (nowT < decisionT && nowT + interval > decisionT)
                    return decisionT - nowT;
            }
            return interval;
        }
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

/// <summary>指数日K同步任务（收盘后；供回放回测构造历史大盘环境）</summary>
public class IndexKlineSyncJob : IScheduledJob
{
    private readonly IndexKlineSyncService _svc;
    public IndexKlineSyncJob(IndexKlineSyncService svc) => _svc = svc;
    public string Name => "index-kline";
    public Task ExecuteAsync(CancellationToken ct) => _svc.SyncAsync(ct: ct);
}

/// <summary>资金流历史同步任务（收盘后；供回放回测补资金面）</summary>
public class CapitalFlowSyncJob : IScheduledJob
{
    private readonly CapitalFlowSyncService _svc;
    public CapitalFlowSyncJob(CapitalFlowSyncService svc) => _svc = svc;
    public string Name => "capital-flow";
    public Task ExecuteAsync(CancellationToken ct) => _svc.SyncAsync(ct: ct);
}

/// <summary>概念炒作点蒸馏任务：LLM 把 selected_reason 提炼成短语写入 concept_digest（幂等，只补空行）</summary>
public class ConceptDigestJob : IScheduledJob
{
    private readonly ConceptDigestService _svc;
    public ConceptDigestJob(ConceptDigestService svc) => _svc = svc;
    public string Name => "concept-digest";
    public Task ExecuteAsync(CancellationToken ct) => _svc.SyncAsync(ct: ct);
}

/// <summary>
/// 每日全策略选股留痕任务（交易日 14:50，尾盘快照刷新后）：所有策略各跑一遍并落库，
/// 只留痕不下单——保证策略记分板每天有完整信号，不依赖人工触发。非交易日自动跳过。
/// </summary>
public class SelectionDailyJob : IScheduledJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITradingCalendar _calendar;
    private readonly MarketSnapshotSyncService _snapshot;
    private readonly IWorkerConfigProvider _config;
    private readonly AIStock.Monitor.IAlertNotifier _notifier;
    private readonly ILogger<SelectionDailyJob> _logger;

    public SelectionDailyJob(IServiceScopeFactory scopeFactory, ITradingCalendar calendar,
        MarketSnapshotSyncService snapshot, IWorkerConfigProvider config,
        AIStock.Monitor.IAlertNotifier notifier, ILogger<SelectionDailyJob> logger)
    {
        _scopeFactory = scopeFactory;
        _calendar = calendar;
        _snapshot = snapshot;
        _config = config;
        _notifier = notifier;
        _logger = logger;
    }

    public string Name => "selection-daily";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!await _calendar.IsTradingDayAsync(DateTime.Today, ct))
        {
            _logger.LogDebug("非交易日，每日选股跳过");
            return;
        }

        // 方案A：尾盘选股前先刷一次盘中快照，保证候选数据=选股时点数据（失败不阻断，沿用旧快照继续选）
        var snapOpt = await _config.GetAsync<MarketSnapshotOptions>(MarketSnapshotOptions.SectionName, ct);
        if (snapOpt.EnableIntraday && snapOpt.RefreshBeforeTailSelection)
        {
            try
            {
                var n = await _snapshot.SyncIntradayAsync(ct);
                _logger.LogInformation("尾盘选股前快照刷新完成：{Count} 只", n);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "尾盘选股前快照刷新失败，沿用现有快照继续选股");
            }
        }

        var runStart = DateTime.Now;
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AIStock.Selection.SelectionDailyService>();
        var run = await svc.RunAllAsync(ct);
        await _notifier.SendAsync(new AIStock.Monitor.Alert(
            AIStock.Monitor.AlertLevel.Info, "尾盘选股报告",
            run.FormatReport($"📊 {DateTime.Today:MM-dd} 尾盘选股（14:50）")), ct);

        // 尾盘选股后：把本次批次去重后入交易候选池（纯规则，含核心逻辑+规则算价，供人工次日手动下单）
        await PopulateCandidatePoolAsync(scope, runStart, ct);
    }

    /// <summary>把本次刚落库的选股批次去重入交易候选池。失败隔离，不影响选股留痕。</summary>
    private async Task PopulateCandidatePoolAsync(IServiceScope scope, DateTime runStart, CancellationToken ct)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AIStock.Infrastructure.Database.Context.AIStockDbContext>();
            var newIds = await db.SelectionResult
                .Where(r => r.RunAt >= runStart)
                .Select(r => r.Id)
                .ToListAsync(ct);
            if (newIds.Count == 0) return;

            var pool = scope.ServiceProvider.GetRequiredService<AIStock.Selection.TradeCandidateService>();
            var n = await pool.PopulateFromBatchesAsync(newIds, ct);
            _logger.LogInformation("尾盘选股后候选池入池完成：{Count} 条", n);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _logger.LogWarning(ex, "候选池入池失败，跳过（不影响选股留痕）"); }
    }
}

/// <summary>
/// 开盘前全策略选股留痕任务（交易日 09:00）：基于昨日完整收盘数据 + 隔夜情报
/// （盘后公告/龙虎榜/资金流/小作文）再选一轮，供竞价/开盘埋伏决策。
/// 与尾盘批次(selection-daily)是并集关系：记分板先入为主，开盘批新增的票按新信号追加。
/// </summary>
public class SelectionPremarketJob : IScheduledJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITradingCalendar _calendar;
    private readonly AIStock.Monitor.IAlertNotifier _notifier;
    private readonly ILogger<SelectionPremarketJob> _logger;

    public SelectionPremarketJob(IServiceScopeFactory scopeFactory, ITradingCalendar calendar,
        AIStock.Monitor.IAlertNotifier notifier, ILogger<SelectionPremarketJob> logger)
    {
        _scopeFactory = scopeFactory;
        _calendar = calendar;
        _notifier = notifier;
        _logger = logger;
    }

    public string Name => "selection-premarket";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!await _calendar.IsTradingDayAsync(DateTime.Today, ct))
        {
            _logger.LogDebug("非交易日，开盘前选股跳过");
            return;
        }
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AIStock.Selection.SelectionDailyService>();
        var run = await svc.RunAllAsync(ct);
        await _notifier.SendAsync(new AIStock.Monitor.Alert(
            AIStock.Monitor.AlertLevel.Info, "开盘前选股早报",
            run.FormatReport($"🌅 {DateTime.Today:MM-dd} 开盘前选股（隔夜情报已纳入）")), ct);
    }
}

/// <summary>
/// 选股信号前向绩效任务（收盘后、K线同步之后）：物化当日各策略最新一批选股为信号，
/// 并对未完成行补算 T+1/T+3/T+5 收益与沪深300超额（幂等，K线到位多少算多少）。
/// </summary>
public class SelectionPerformanceJob : IScheduledJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    public SelectionPerformanceJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;
    public string Name => "selection-performance";

    public async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AIStock.Selection.Performance.SelectionPerformanceService>();
        await svc.SyncAsync(ct);
    }
}
