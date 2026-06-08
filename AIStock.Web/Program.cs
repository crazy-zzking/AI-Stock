using AIStock.Core.Interfaces;
using AIStock.Data;
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
using AIStock.Monitor;
using AIStock.Orchestrator;
using AIStock.Prompt;
using AIStock.Prompt.Services;
using AIStock.Risk;
using AIStock.Selection;
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
// 重新追加环境变量与命令行，确保其优先级高于 user.json（命令行最高，便于 docker 环境变量覆盖）
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

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
builder.Services.AddRiskServices(builder.Configuration);

// 注册选股服务
builder.Services.AddSelectionServices();

// 选股 LLM 复评：Channel 队列（替换默认空实现）+ 后台消费服务 + 配置
builder.Services.Configure<AIStock.Selection.Review.SelectionReviewOptions>(
    builder.Configuration.GetSection(AIStock.Selection.Review.SelectionReviewOptions.SectionName));
builder.Services.AddSingleton<AIStock.Web.Services.SelectionReviewQueue>();
builder.Services.AddSingleton<AIStock.Selection.Review.ISelectionReviewQueue>(
    sp => sp.GetRequiredService<AIStock.Web.Services.SelectionReviewQueue>());
builder.Services.AddHostedService<AIStock.Web.Services.SelectionReviewBackgroundService>();

// 注册执行服务
builder.Services.AddExecutionServices(builder.Configuration);

// 注册编排服务
builder.Services.AddOrchestratorServices();

// 注册尾盘自动下单（选股→OrderManager 衔接 + 定时触发；默认关闭 / 仅 DryRun）
// TailStopLossService：盘中实时止损监控（每 60 秒，与尾盘定时解耦）
// TailMarketBuyHostedService：14:55 到期卖出 + 新买入
builder.Services.Configure<AIStock.Web.TailBuyOptions>(builder.Configuration.GetSection(AIStock.Web.TailBuyOptions.SectionName));
builder.Services.AddScoped<AIStock.Web.Services.TailMarketBuyService>();
builder.Services.AddScoped<AIStock.Web.Services.TailSellService>();
builder.Services.AddHostedService<AIStock.Web.Services.TailStopLossService>();
builder.Services.AddHostedService<AIStock.Web.Services.TailMarketBuyHostedService>();

// 注册Prompt Registry服务
builder.Services.AddPromptServices();

// 注册Agent Memory服务
builder.Services.AddMemoryServices();

// 注册监控告警服务
builder.Services.Configure<AIStock.Monitor.MonitorOptions>(
    builder.Configuration.GetSection(AIStock.Monitor.MonitorOptions.SectionName));
builder.Services.AddMonitorServices();
builder.Services.AddHostedService<AIStock.Web.Services.MonitorBackgroundService>();
builder.Services.AddScoped<AIStock.Web.Services.DailyReviewService>();
builder.Services.AddHostedService<AIStock.Web.Services.DailyReviewBackgroundService>();

// 注册数据源Provider（HttpClient/Resolver/各Provider，统一扩展，与 Worker 共用）
builder.Services.AddDataProviders(builder.Configuration);

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
app.Services.InitializeDataProviders();

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
