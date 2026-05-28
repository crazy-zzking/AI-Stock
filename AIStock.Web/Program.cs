using AIStock.Core.Interfaces;
using AIStock.Data.Providers;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Data.Providers.Sanhu;
using AIStock.Data.Providers.Tencent;
using AIStock.Data.Providers.Tdx;
using AIStock.EventEngine;
using AIStock.Execution;
using AIStock.Feature;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.MessageBus;
using AIStock.Intelligence;
using AIStock.Knowledge;
using AIStock.LLM;
using AIStock.Risk;
using AIStock.Strategy;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 加载用户配置文件（如果存在）
builder.Configuration.AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: true);

// 配置Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ai-stock-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 添加服务
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

// 配置MySQL
var connectionString = builder.Configuration.GetConnectionString("MySQL");
builder.Services.AddDbContext<AIStockDbContext>(options =>
    options.UseMySQL(connectionString!));

// 配置Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnection!));

// 注册消息总线
builder.Services.AddSingleton<IMessageBus, RedisMessageBus>();

// 注册LLM服务
builder.Services.AddLLMServices();

// 注册情报服务
builder.Services.AddIntelligenceServices();

// 注册事件引擎服务
builder.Services.AddEventEngineServices();

// 注册知识图谱服务
builder.Services.AddKnowledgeServices();

// 注册特征工程服务
builder.Services.AddFeatureServices();

// 注册策略服务
builder.Services.AddStrategyServices();

// 注册风控服务
builder.Services.AddRiskServices();

// 注册执行服务
builder.Services.AddExecutionServices();

// 注册数据源Provider
builder.Services.AddSingleton<IDataProviderResolver, DataProviderResolver>();

// 注册HttpClient
builder.Services.AddHttpClient();

// 注册各数据源Provider
builder.Services.AddSingleton<IDataProvider>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<SanhuProvider>>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var config = builder.Configuration.GetSection("DataProviders:Sanhu");
    return new SanhuProvider(
        logger,
        httpClient,
        config["BaseUrl"]!,
        config["Token"]!);
});

builder.Services.AddSingleton<IDataProvider>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EastmoneyProvider>>();
    var config = builder.Configuration.GetSection("Proxy");
    
    // 检查是否启用隧道代理
    var useTunnelProxy = config.GetValue<bool>("UseTunnelProxy");
    HttpClient httpClient;
    
    if (useTunnelProxy)
    {
        var tunnelHost = config["TunnelHost"] ?? "c360.kdltps.com";
        var tunnelPort = config.GetValue<int>("TunnelPort", 15818);
        var tunnelUsername = config["TunnelUsername"] ?? "";
        var tunnelPassword = config["TunnelPassword"] ?? "";
        
        httpClient = EastmoneyProvider.CreateHttpClientWithTunnelProxy(
            tunnelHost, tunnelPort, tunnelUsername, tunnelPassword);
        
        logger.LogInformation("Eastmoney provider using tunnel proxy: {Host}:{Port}", tunnelHost, tunnelPort);
    }
    else
    {
        httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    }
    
    return new EastmoneyProvider(logger, httpClient);
});

builder.Services.AddSingleton<IDataProvider>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TencentProvider>>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    return new TencentProvider(logger, httpClient);
});

builder.Services.AddSingleton<IDataProvider>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TdxProvider>>();
    // 通达信服务器配置（可从配置文件读取）
    var host = builder.Configuration["Tdx:Host"] ?? "119.147.212.81";
    var port = builder.Configuration.GetValue<int>("Tdx:Port", 7709);
    return new TdxProvider(logger, host, port);
});

// 配置CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 配置中间件
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 不使用HTTPS重定向（内部API服务）
// app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();
app.MapControllers();

// 初始化数据库
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
    dbContext.Database.EnsureCreated();
}

// 注册Provider到Resolver
using (var scope = app.Services.CreateScope())
{
    var resolver = scope.ServiceProvider.GetRequiredService<IDataProviderResolver>();
    var providers = scope.ServiceProvider.GetServices<IDataProvider>();
    foreach (var provider in providers)
    {
        resolver.RegisterProvider(provider);
    }
}

// 检查 Playwright 是否已安装（可选）
if (builder.Configuration.GetValue<bool>("Playwright:CheckOnStartup"))
{
    try
    {
        await AIStock.Intelligence.PlaywrightInitializer.EnsureInstalledAsync();
    }
    catch (Exception ex)
    {
        Log.Warning("Playwright check failed: {Message}", ex.Message);
    }
}

app.Run();
