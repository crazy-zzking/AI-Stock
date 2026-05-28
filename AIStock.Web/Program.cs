using AIStock.Core.Interfaces;
using AIStock.Data.Providers;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Data.Providers.Sanhu;
using AIStock.Data.Providers.Tencent;
using AIStock.Data.Providers.Tdx;
using AIStock.EventEngine;
using AIStock.Execution;
using AIStock.Feature;
using AIStock.GraphRAG;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.MessageBus;
using AIStock.Intelligence;
using AIStock.Knowledge;
using AIStock.LLM;
using AIStock.Memory;
using AIStock.Orchestrator;
using AIStock.Prompt;
using AIStock.Prompt.Services;
using AIStock.Risk;
using AIStock.Strategy;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

// 配置 OpenTelemetry 可观测性
var otelEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:Endpoint");
var serviceName = "AIStock";
var serviceVersion = "1.0.0";

if (!string.IsNullOrEmpty(otelEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: serviceName,
            serviceVersion: serviceVersion))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("AIStock.Orchestrator", "AIStock.LLM", "AIStock.Strategy")
            .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter());

    // 将 OTLP 也接入 Serilog 日志管道
    builder.Logging.AddOpenTelemetry(o =>
    {
        o.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion));
        o.IncludeFormattedMessage = true;
        o.IncludeScopes = true;
        o.ParseStateValues = true;
        o.AddOtlpExporter(exp => exp.Endpoint = new Uri(otelEndpoint));
    });
}
else
{
    // 无 OTLP 端点时仅启用 Prometheus metrics（本地开发模式）
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: serviceName,
            serviceVersion: serviceVersion))
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter());
}

// 配置MySQL
var connectionString = builder.Configuration.GetConnectionString("MySQL");
builder.Services.AddDbContext<AIStockDbContext>(options =>
    options.UseMySql(connectionString!, ServerVersion.AutoDetect(connectionString!)));

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

// 注册图谱增强RAG服务
builder.Services.AddGraphRAGServices();

// 注册特征工程服务
builder.Services.AddFeatureServices();

// 注册策略服务
builder.Services.AddStrategyServices();

// 注册风控服务
builder.Services.AddRiskServices();

// 注册执行服务
builder.Services.AddExecutionServices();

// 注册编排服务
builder.Services.AddOrchestratorServices();

// 注册Prompt Registry服务
builder.Services.AddPromptServices();

// 注册Agent Memory服务
builder.Services.AddMemoryServices();

// 注册数据源Provider
builder.Services.AddSingleton<IDataProviderResolver, DataProviderResolver>();

// 注册HttpClient（带重试策略）
builder.Services.AddHttpClient("default")
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(1);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 10;
    });

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
        config["Token"]!,
        config["MyKey"] ?? "");
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

// 全局异常处理
app.UseExceptionHandler(error =>
{
    error.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = context.Response.StatusCode,
            Title = exception?.Message ?? "Internal Server Error",
            Detail = app.Environment.IsDevelopment() ? exception?.StackTrace : null,
            Instance = context.Request.Path
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});

// 不使用HTTPS重定向（内部API服务）
// app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

// 初始化数据库（使用Migration）
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
    await dbContext.Database.MigrateAsync();

    // 自动导入默认Prompt（仅当表为空时）
    var promptRegistry = scope.ServiceProvider.GetRequiredService<IPromptRegistry>();
    await promptRegistry.SeedDefaultPromptsAsync();
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
