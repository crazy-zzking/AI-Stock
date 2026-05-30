using AIStock.Data;
using AIStock.Infrastructure.Database.Context;
using AIStock.Worker;
using AIStock.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// 加载用户配置文件（如果存在）
builder.Configuration.AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: true);

// 配置 Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ai-stock-worker-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddSerilog();

// 配置 MySQL
var connectionString = builder.Configuration.GetConnectionString("MySQL");
builder.Services.AddDbContext<AIStockDbContext>(options =>
    options.UseMySql(connectionString!, ServerVersion.AutoDetect(connectionString!)));

// 注册数据源 Provider（与 Web 共用同一扩展）
builder.Services.AddDataProviders(builder.Configuration);

// 数据同步配置与服务
builder.Services.Configure<DataSyncOptions>(builder.Configuration.GetSection(DataSyncOptions.SectionName));
builder.Services.AddSingleton<DataSyncService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// 启动时将 Provider 灌入 Resolver
host.Services.InitializeDataProviders();

host.Run();
