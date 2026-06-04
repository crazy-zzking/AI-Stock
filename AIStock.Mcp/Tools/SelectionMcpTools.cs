using System.ComponentModel;
using System.Text.Json;
using AIStock.Core.Models;
using AIStock.Selection;
using AIStock.Selection.Backtest;
using AIStock.Selection.Strategies;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace AIStock.Mcp.Tools;

/// <summary>
/// 选股策略优化 MCP 工具集。大模型通过这些工具自主优化：
/// 读默认/生效参数 → 用候选参数回放回测 → 对比胜率/盈亏比/回撤 → 保存并激活最优版本。
/// 每个工具自建 DI scope，安全使用 scoped 的 DbContext。
/// </summary>
[McpServerToolType]
public static class SelectionMcpTools
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [McpServerTool(Name = "list_strategies"), Description("列出可用选股策略(key/名称/说明/适用市场环境)。优化前先了解有哪些策略。")]
    public static string ListStrategies(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var strategies = scope.ServiceProvider.GetServices<ISelectionStrategy>();
        return JsonSerializer.Serialize(
            strategies.Select(s => new { s.Key, s.Name, s.Description, s.PreferredRegime }), Json);
    }

    [McpServerTool(Name = "get_default_config"), Description("获取代码内置默认选股参数(SelectionCriteria JSON)，作为调参起点。含阈值与 8 因子权重。")]
    public static string GetDefaultConfig()
        => JsonSerializer.Serialize(SelectionConfigService.GetDefaultCriteria(), Json);

    [McpServerTool(Name = "get_active_config"), Description("获取某策略当前生效的选股参数(SelectionCriteria JSON)。strategy: lowdip/trend/theme。")]
    public static async Task<string> GetActiveConfig(
        IServiceScopeFactory scopeFactory,
        [Description("策略键 lowdip/trend/theme")] string strategy)
    {
        using var scope = scopeFactory.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<SelectionConfigService>();
        return JsonSerializer.Serialize(await cfg.GetActiveCriteriaAsync(strategy), Json);
    }

    [McpServerTool(Name = "replay_backtest"), Description(
        "参数回放回测(核心评估工具)：用给定参数在历史区间逐交易日重跑选股并回测，返回胜率/平均·中位收益/" +
        "盈亏比/最大回撤/浮盈浮亏等。优化目标关注：盈亏比、胜率，并控制最大回撤。反复调整 criteriaJson 调用本工具对比。")]
    public static async Task<string> ReplayBacktest(
        IServiceScopeFactory scopeFactory,
        [Description("策略键 lowdip/trend/theme")] string strategy,
        [Description("SelectionCriteria 的 JSON；传空字符串则用该策略当前生效参数")] string criteriaJson,
        [Description("回测开始日 yyyy-MM-dd；空则结束日往前30天")] string from,
        [Description("回测结束日 yyyy-MM-dd；空则今天")] string to,
        [Description("持有交易日数，默认5")] int holdDays = 5)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        SelectionCriteria criteria;
        if (string.IsNullOrWhiteSpace(criteriaJson))
            criteria = await sp.GetRequiredService<SelectionConfigService>().GetActiveCriteriaAsync(strategy);
        else
            criteria = JsonSerializer.Deserialize<SelectionCriteria>(criteriaJson)
                ?? throw new ArgumentException("criteriaJson 解析失败");

        var toD = DateTime.TryParse(to, out var t) ? t : DateTime.Today;
        var fromD = DateTime.TryParse(from, out var f) ? f : toD.AddDays(-30);

        var replay = sp.GetRequiredService<ReplayBacktestService>();
        var report = await replay.BacktestParamsAsync(strategy, criteria, fromD, toD, new BacktestConfig { HoldDays = holdDays });
        return JsonSerializer.Serialize(report, Json);
    }

    [McpServerTool(Name = "backtest_history"), Description("回测已落库的历史选股结果(真实选过的标的)，返回表现汇总。用于了解现状基线。")]
    public static async Task<string> BacktestHistory(
        IServiceScopeFactory scopeFactory,
        [Description("持有交易日数，默认5")] int holdDays = 5)
    {
        using var scope = scopeFactory.CreateScope();
        var bt = scope.ServiceProvider.GetRequiredService<BacktestService>();
        var report = await bt.BacktestHistoryAsync(new BacktestConfig { HoldDays = holdDays });
        return JsonSerializer.Serialize(report, Json);
    }

    [McpServerTool(Name = "save_config"), Description(
        "保存一组选股参数为新版本。activate=true 则立即生效用于实际选股(请仅在回测确认更优后激活)。" +
        "strategy 作为配置名(lowdip/trend/theme)，同名+版本号已存在则覆盖。")]
    public static async Task<string> SaveConfig(
        IServiceScopeFactory scopeFactory,
        [Description("策略键 lowdip/trend/theme")] string strategy,
        [Description("版本号，如 v2.0")] string version,
        [Description("SelectionCriteria 的 JSON")] string criteriaJson,
        [Description("备注：本次调参依据与回测结论")] string remark,
        [Description("是否立即激活生效")] bool activate)
    {
        using var scope = scopeFactory.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<SelectionConfigService>();
        var criteria = JsonSerializer.Deserialize<SelectionCriteria>(criteriaJson)
            ?? throw new ArgumentException("criteriaJson 解析失败");
        var saved = await cfg.SaveAsync(strategy, version, criteria, remark, activate);
        return JsonSerializer.Serialize(new { saved.Id, saved.Name, saved.Version, saved.IsActive }, Json);
    }

    [McpServerTool(Name = "list_configs"), Description("列出所有已保存的选股配置版本(含是否生效)。")]
    public static async Task<string> ListConfigs(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<SelectionConfigService>();
        var list = await cfg.ListAsync();
        return JsonSerializer.Serialize(
            list.Select(c => new { c.Id, c.Name, c.Version, c.IsActive, c.Remark, c.UpdatedAt }), Json);
    }
}
