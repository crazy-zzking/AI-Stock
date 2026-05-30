using AIStock.Data;
using AIStock.EventEngine;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.MessageBus;
using AIStock.Intelligence;
using AIStock.LLM;
using AIStock.Core.Interfaces;
using AIStock.Worker;
using AIStock.Worker.Services;
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
    .MinimumLevel.Override("Polly", Serilog.Events.LogEventLevel.Warning)
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

// Redis（消息总线依赖）
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
    builder.Services.AddSingleton<IMessageBus, RedisMessageBus>();
}

// 内存缓存（LLM 模型配置缓存依赖）
builder.Services.AddMemoryCache();

// LLM / 情报 / 事件引擎服务（情报抽取入库链路）
builder.Services.AddLLMServices();
builder.Services.AddIntelligenceServices();
builder.Services.AddEventEngineServices();

// 数据同步配置与服务
builder.Services.Configure<DataSyncOptions>(builder.Configuration.GetSection(DataSyncOptions.SectionName));
builder.Services.AddSingleton<DataSyncService>();
builder.Services.AddHostedService<Worker>();

// 情报采集配置与服务
builder.Services.Configure<IntelligenceSyncOptions>(builder.Configuration.GetSection(IntelligenceSyncOptions.SectionName));
builder.Services.AddSingleton<IntelligenceSyncService>();
builder.Services.AddHostedService<IntelligenceWorker>();

var host = builder.Build();

// 启动时应用数据库迁移（确保表结构最新）
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
    await db.Database.MigrateAsync();
}

// 启动时将 Provider 灌入 Resolver
host.Services.InitializeDataProviders();

host.Run();
