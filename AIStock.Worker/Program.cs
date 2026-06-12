using AIStock.Data;
using AIStock.EventEngine;
using AIStock.Feature;
using AIStock.Infrastructure.Configuration;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.MessageBus;
using AIStock.Intelligence;
using AIStock.LLM;
using AIStock.Core.Interfaces;
using AIStock.Worker;
using AIStock.Worker.Services;
using AIStock.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// 加载用户配置文件（如果存在）
builder.Configuration.AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: true);
// 重新追加环境变量与命令行，确保其优先级高于 user.json（命令行最高）
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

// 配置 Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Polly", Serilog.Events.LogEventLevel.Error)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ai-stock-worker-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddSerilog();

// 防崩溃连带：单个 BackgroundService 未捕获异常默认会停掉整个宿主，改为忽略（仅该服务受影响）
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// 配置 MySQL
var connectionString = builder.Configuration.GetConnectionString("MySQL");
builder.Services.AddDbContext<AIStockDbContext>(options =>
    options.UseMySql(connectionString!, ServerVersion.AutoDetect(connectionString!)));

// 注册数据源 Provider（与 Web 共用同一扩展）
builder.Services.AddDataProviders(builder.Configuration);

// 技术指标计算（市场快照采集依赖）
builder.Services.AddFeatureServices();

// Redis（消息总线依赖）
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
    builder.Services.AddSingleton<IMessageBus, RedisMessageBus>();
}

// 内存缓存（LLM 模型配置缓存依赖）
builder.Services.AddMemoryCache();

// Worker 任务配置中心（前端可配置：调度 + 业务参数，DB 存储 + TTL 热读）
builder.Services.AddSingleton<IWorkerConfigProvider, WorkerConfigService>();

// LLM / 情报 / 事件引擎服务（情报抽取入库链路）
builder.Services.AddLLMServices();
builder.Services.AddIntelligenceServices();
builder.Services.AddEventEngineServices();

// 业务参数配置与服务实现
builder.Services.Configure<DataSyncOptions>(builder.Configuration.GetSection(DataSyncOptions.SectionName));
builder.Services.AddSingleton<DataSyncService>();
builder.Services.Configure<IntelligenceSyncOptions>(builder.Configuration.GetSection(IntelligenceSyncOptions.SectionName));
builder.Services.Configure<KnowledgeStarOptions>(builder.Configuration.GetSection(KnowledgeStarOptions.SectionName));
builder.Services.AddSingleton<IntelligenceSyncService>();
builder.Services.AddSingleton<PositionCacheService>();
builder.Services.Configure<MarketSnapshotOptions>(builder.Configuration.GetSection(MarketSnapshotOptions.SectionName));
builder.Services.AddSingleton<MarketSnapshotSyncService>();
builder.Services.AddSingleton<DragonTigerSyncService>();
builder.Services.AddSingleton<IndexKlineSyncService>();
builder.Services.AddSingleton<CapitalFlowSyncService>();
builder.Services.AddSingleton<ConceptDigestService>();
// 选股信号前向绩效（Scoped：依赖 DbContext，任务内开作用域使用）
builder.Services.AddScoped<AIStock.Selection.Performance.SelectionPerformanceService>();

// 调度：每个后台任务独立注册，调度参数由 worker_config 表 Jobs 段（前端可配）热读
builder.Services.AddSingleton<IScheduledJob, StockBaseSyncJob>();
builder.Services.AddSingleton<IScheduledJob, StockDetailSyncJob>();
builder.Services.AddSingleton<IScheduledJob, KlineSyncJob>();
builder.Services.AddSingleton<IScheduledJob, NewsCollectJob>();
builder.Services.AddSingleton<IScheduledJob, AnnouncementCollectJob>();
builder.Services.AddSingleton<IScheduledJob, ReportCollectJob>();
builder.Services.AddSingleton<IScheduledJob, KnowledgeStarCollectJob>();
builder.Services.AddSingleton<IScheduledJob, GraphPromoteJob>();
builder.Services.AddSingleton<IScheduledJob, PositionCacheJob>();
builder.Services.AddSingleton<IScheduledJob, MarketSnapshotSyncJob>();
builder.Services.AddSingleton<IScheduledJob, DragonTigerCollectJob>();
builder.Services.AddSingleton<IScheduledJob, IndexKlineSyncJob>();
builder.Services.AddSingleton<IScheduledJob, CapitalFlowSyncJob>();
builder.Services.AddSingleton<IScheduledJob, ConceptDigestJob>();
builder.Services.AddSingleton<IScheduledJob, SelectionPerformanceJob>();
builder.Services.Configure<GraphPromotionOptions>(
    builder.Configuration.GetSection(GraphPromotionOptions.SectionName));
// 运行协调器（单飞防并发 + 运行态落库）与手动「立即运行」轮询器
builder.Services.AddSingleton<JobRunCoordinator>();
builder.Services.AddHostedService<ManualRunPoller>();
builder.Services.AddHostedService<JobScheduler>();

var host = builder.Build();

// 启动时应用数据库迁移（确保表结构最新）
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
    await db.Database.MigrateAsync();
}

// 任务配置种子化：把 appsettings 各段默认值固化进 worker_config（仅当该段尚无 DB 行）。
// 此后 DB 即权威源，前端改动热生效；appsettings 仅作首次种子来源。
{
    var cfg = host.Services.GetRequiredService<IWorkerConfigProvider>();
    var c = builder.Configuration;
    await cfg.EnsureSeededAsync(JobSchedulerOptions.SectionName,
        c.GetSection(JobSchedulerOptions.SectionName).Get<Dictionary<string, JobOptions>>() ?? new());
    await cfg.EnsureSeededAsync(DataSyncOptions.SectionName,
        c.GetSection(DataSyncOptions.SectionName).Get<DataSyncOptions>() ?? new());
    await cfg.EnsureSeededAsync(IntelligenceSyncOptions.SectionName,
        c.GetSection(IntelligenceSyncOptions.SectionName).Get<IntelligenceSyncOptions>() ?? new());
    await cfg.EnsureSeededAsync(MarketSnapshotOptions.SectionName,
        c.GetSection(MarketSnapshotOptions.SectionName).Get<MarketSnapshotOptions>() ?? new());
    await cfg.EnsureSeededAsync(KnowledgeStarOptions.SectionName,
        c.GetSection(KnowledgeStarOptions.SectionName).Get<KnowledgeStarOptions>() ?? new());
    await cfg.EnsureSeededAsync(GraphPromotionOptions.SectionName,
        c.GetSection(GraphPromotionOptions.SectionName).Get<GraphPromotionOptions>() ?? new());
}

// 清零残留的任务运行态（防上次 Worker 崩溃后卡死为"运行中"）
await host.Services.GetRequiredService<JobRunCoordinator>().ResetRunningFlagsAsync();

// 启动时将 Provider 灌入 Resolver
host.Services.InitializeDataProviders();

host.Run();
