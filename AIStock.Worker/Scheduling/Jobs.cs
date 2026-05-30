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

/// <summary>知识星球采集任务（低频，防封）</summary>
public class KnowledgeStarCollectJob : IScheduledJob
{
    private readonly IntelligenceSyncService _sync;
    public KnowledgeStarCollectJob(IntelligenceSyncService sync) => _sync = sync;
    public string Name => "knowledge-star";
    public Task ExecuteAsync(CancellationToken ct) => _sync.SyncKnowledgeStarAsync(ct);
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
