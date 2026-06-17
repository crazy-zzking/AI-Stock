using AIStock.Core.Models;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 出场规则预设组合。每种预设返回一个按 Priority 排序好的规则列表，
/// 可直接赋值给 BacktestConfig.ExitRules。
/// </summary>
public static class ExitRulePresets
{
    /// <summary>
    /// 均衡默认：移动止损(5%) + 移动止盈(浮盈10%激活/回落3%) + 跌破MA10出场 + 最多持有 10 天。
    /// 作为用户不主动配置时的兜底预设——既锁利又控回撤，适配大多数短线埋伏型选股。
    /// </summary>
    public static List<ExitRule> Default() => new()
    {
        ExitRules.TrailingStop(5, priority: 1),
        ExitRules.TrailingTakeProfit(10, 3, priority: 2),
        ExitRules.MaExit(10, priority: 3),
        ExitRules.TimeExit(10, priority: 99),
    };

    /// <summary>
    /// 趋势跟随：移动止损(5%) + MA20 死叉出场 + 最多持有 10 天。
    /// 适合趋势型策略——让利润奔跑，但从最高点回落 5% 或跌破 MA20 就走。
    /// </summary>
    public static List<ExitRule> TrendFollow() => new()
    {
        ExitRules.TrailingStop(5, priority: 1),
        ExitRules.MaExit(20, priority: 2),
        ExitRules.TimeExit(10, priority: 99),
    };

    /// <summary>
    /// 波段交易：固定止盈(8%) + 固定止损(3%) + 最多持有 5 天。
    /// 适合短线波段——赚 8% 或亏 3% 必走，不恋战。
    /// </summary>
    public static List<ExitRule> Swing() => new()
    {
        ExitRules.FixedTakeProfit(8, priority: 1),
        ExitRules.FixedStopLoss(3, priority: 2),
        ExitRules.TimeExit(5, priority: 99),
    };

    /// <summary>
    /// 网格交易：固定止盈(2%) + 固定止损(2%) + 最多持有 20 天。
    /// 窄幅网格——小赚小亏快进快出，靠高频覆盖摩擦成本。
    /// </summary>
    public static List<ExitRule> Grid() => new()
    {
        ExitRules.FixedTakeProfit(2, priority: 1),
        ExitRules.FixedStopLoss(2, priority: 2),
        ExitRules.TimeExit(20, priority: 99),
    };

    /// <summary>
    /// ATR 波段：ATR 动态止损(2×ATR) + 移动止损(3%) + 最多持有 15 天。
    /// 适合波动率较高的标的——止损线随波动率自适应。
    /// </summary>
    public static List<ExitRule> AtrSwing() => new()
    {
        ExitRules.AtrStop(2.0m, priority: 1),
        ExitRules.TrailingStop(3, priority: 2),
        ExitRules.TimeExit(15, priority: 99),
    };

    /// <summary>
    /// 按预设名称解析规则列表。不支持时返回 null。
    /// </summary>
    public static List<ExitRule>? Resolve(string? presetName) => presetName?.ToLowerInvariant() switch
    {
        "default" => Default(),
        "trend_follow" or "trendfollow" => TrendFollow(),
        "swing" => Swing(),
        "grid" => Grid(),
        "atr_swing" or "atrswing" => AtrSwing(),
        _ => null,
    };
}
