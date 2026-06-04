using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 选股横截面上下文构造（纯计算，供回放回测对任意历史交易日复用）。
/// 题材/板块逻辑与 StockSelectionService 一致；大盘环境为"无指数简化版"
/// （回放不调外部指数接口，用全市场广度 + 涨停/跌停近似），描述带 [回放] 前缀以区分。
/// </summary>
public static class SelectionContextBuilder
{
    /// <summary>当日热门题材：活跃股（涨停/大涨/放量）扎堆的概念 → 活跃股数（≥2 才算）。</summary>
    public static Dictionary<string, int> ComputeHotConcepts(
        IReadOnlyList<DailyMarketSnapshotEntity> dayShots,
        IReadOnlyDictionary<string, List<string>> conceptsByCode)
    {
        var activeCodes = dayShots
            .Where(s => s.IsLimitUp || s.ChangePercent >= 5m || (s.VolumeRatio >= 2m && s.ChangePercent > 0))
            .Select(s => s.Code)
            .ToHashSet();
        if (activeCodes.Count == 0) return new();

        var counter = new Dictionary<string, HashSet<string>>();
        foreach (var (code, concepts) in conceptsByCode)
        {
            if (!activeCodes.Contains(code)) continue;
            foreach (var c in concepts)
            {
                if (!counter.TryGetValue(c, out var set)) counter[c] = set = new HashSet<string>();
                set.Add(code);
            }
        }
        return counter.Where(kv => kv.Value.Count >= 2).ToDictionary(kv => kv.Key, kv => kv.Value.Count);
    }

    /// <summary>板块强度：行业平均涨幅分位（0-100）；行业内不足 3 只记中性。</summary>
    public static Dictionary<string, decimal> ComputeSectorStrength(
        IReadOnlyList<DailyMarketSnapshotEntity> dayShots,
        IReadOnlyDictionary<string, string> industryByCode)
    {
        var changeByCode = dayShots.ToDictionary(s => s.Code, s => s.ChangePercent);
        var industryAvg = industryByCode
            .Where(kv => changeByCode.ContainsKey(kv.Key))
            .GroupBy(kv => kv.Value)
            .Select(g => new { Industry = g.Key, Codes = g.Select(x => x.Key).ToList() })
            .Where(x => x.Codes.Count >= 3)
            .Select(x => new { x.Industry, Avg = x.Codes.Average(c => changeByCode[c]) })
            .OrderBy(x => x.Avg)
            .ToList();

        var strength = new Dictionary<string, decimal>();
        for (int i = 0; i < industryAvg.Count; i++)
            strength[industryAvg[i].Industry] = industryAvg.Count <= 1
                ? 50m
                : Math.Round((decimal)i / (industryAvg.Count - 1) * 100m, 1);
        return strength;
    }

    /// <summary>
    /// 回放大盘环境：给定"截至当日"的历史指数行情 + 当日全市场广度，走与实盘一致的 RegimeEvaluator。
    /// indices 为空（指数历史未采集）时优雅降级为仅广度判断。
    /// </summary>
    public static MarketRegime BuildRegime(
        IReadOnlyList<DailyMarketSnapshotEntity> dayShots, IReadOnlyList<IndexQuote> indices)
    {
        var regime = new MarketRegime();
        if (dayShots.Count == 0) return regime;

        regime.AdvanceRatio = Math.Round((decimal)dayShots.Count(s => s.ChangePercent > 0) / dayShots.Count, 2);
        regime.LimitUpCount = dayShots.Count(s => s.IsLimitUp);
        regime.LimitDownCount = dayShots.Count(s => s.ChangePercent <= -9.8m);
        regime.Indices = indices.ToList();

        var (level, kind, rec, _) = RegimeEvaluator.Evaluate(
            indices, regime.AdvanceRatio, regime.LimitUpCount, regime.LimitDownCount, dayShots.Count);
        regime.Level = level;
        regime.Kind = kind;
        regime.RecommendedStrategy = rec;
        regime.Description = $"[回放]{(indices.Count > 0 ? "指数+" : "")}广度 {regime.AdvanceRatio:P0}，" +
            $"涨停 {regime.LimitUpCount}/跌停 {regime.LimitDownCount}";
        return regime;
    }

    /// <summary>无指数简化版（兼容旧调用）：等价于 BuildRegime(dayShots, 空指数)。</summary>
    public static MarketRegime BuildRegimeFromBreadth(IReadOnlyList<DailyMarketSnapshotEntity> dayShots)
        => BuildRegime(dayShots, Array.Empty<IndexQuote>());
}
