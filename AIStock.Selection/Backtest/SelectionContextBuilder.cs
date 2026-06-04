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

    /// <summary>无指数简化大盘环境：仅用全市场广度 + 涨停/跌停近似 Level/Kind（回放用）。</summary>
    public static MarketRegime BuildRegimeFromBreadth(IReadOnlyList<DailyMarketSnapshotEntity> dayShots)
    {
        var regime = new MarketRegime();
        if (dayShots.Count == 0) return regime;

        regime.AdvanceRatio = Math.Round((decimal)dayShots.Count(s => s.ChangePercent > 0) / dayShots.Count, 2);
        regime.LimitUpCount = dayShots.Count(s => s.IsLimitUp);
        regime.LimitDownCount = dayShots.Count(s => s.ChangePercent <= -9.8m);

        // Level（无指数，仅广度）：用于策略弱市收紧/强市放宽
        var score = 0;
        if (regime.AdvanceRatio > 0.55m) score++;
        else if (regime.AdvanceRatio is > 0m and < 0.4m) score--;
        if (regime.LimitDownCount >= 20 && regime.LimitDownCount > regime.LimitUpCount) score--;
        regime.Level = score >= 1 ? MarketRegimeLevel.Strong
            : score <= -1 ? MarketRegimeLevel.Weak
            : MarketRegimeLevel.Neutral;

        var cls = MarketRegimeClassifier.Classify(
            avgIndexChange: 0m, indicesAboveMa20: 0, indexCount: 0,
            advanceRatio: regime.AdvanceRatio, limitUpCount: regime.LimitUpCount,
            limitDownCount: regime.LimitDownCount, totalStocks: dayShots.Count);
        regime.Kind = cls.Kind;
        regime.RecommendedStrategy = cls.RecommendedStrategy;
        regime.Description = $"[回放]广度 {regime.AdvanceRatio:P0}，涨停 {regime.LimitUpCount}/跌停 {regime.LimitDownCount}";
        return regime;
    }
}
