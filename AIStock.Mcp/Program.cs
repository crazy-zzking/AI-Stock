using AIStock.Infrastructure.Database.Context;
using AIStock.Selection;
using AIStock.Selection.Backtest;
using AIStock.Selection.Narration;
using AIStock.Selection.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// AIStock 策略优化 MCP Server（stdio）。供支持 MCP 的大模型客户端（Claude Desktop 等）
// 自主调用工具：读参数 → 回放回测 → 对比 → 存/激活最优参数，实现策略自动优化。
var builder = Host.CreateApplicationBuilder(args);

// stdio 传输占用 stdout，所有日志必须走 stderr，否则会污染 MCP 协议帧
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// 数据库连接：优先 appsettings，其次环境变量 ConnectionStrings__MySQL（Claude Desktop 配置里注入）
var conn = builder.Configuration.GetConnectionString("MySQL");
if (string.IsNullOrWhiteSpace(conn))
    throw new InvalidOperationException(
        "缺少 MySQL 连接串。请在 AIStock.Mcp/appsettings.json 的 ConnectionStrings:MySQL 填写，" +
        "或通过环境变量 ConnectionStrings__MySQL 提供（Claude Desktop mcpServers.env）。");

builder.Services.AddDbContext<AIStockDbContext>(o => o.UseMySql(conn, ServerVersion.Parse("8.0.0")));

// 选股能力（仅注册回放/回测/配置所需，不含依赖外部行情源的 StockSelectionService）
builder.Services.AddScoped<ILogicNarrator, RuleLogicNarrator>();
builder.Services.AddScoped<StockSelectionEngine>();
builder.Services.AddScoped<ISelectionStrategy, LowDipStrategy>();
builder.Services.AddScoped<ISelectionStrategy, TrendStrategy>();
builder.Services.AddScoped<ISelectionStrategy, ThemeStrategy>();
builder.Services.AddScoped<SelectionConfigService>();
builder.Services.AddScoped<BacktestService>();
builder.Services.AddScoped<ReplayBacktestService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
