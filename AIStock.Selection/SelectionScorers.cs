using System.Text.Json;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection;

/// <summary>
/// 共享因子打分器（纯函数，0-100）。各策略（LowDip/Trend/Theme）复用同一套单因子评分，
/// 仅在"权重组合 / 硬过滤 / 特殊加成"上体现策略差异，避免打分口径漂移。
/// 内容由 StockSelectionEngine 抽取而来，行为与历史一致（现有引擎单测为行为锁）。
/// </summary>
public static class SelectionScorers
{
    /// <summary>资金面：主力净流入强度 + 连续净流入加成（连续多日才是真建仓）。</summary>
    public static decimal Capital(DailyMarketSnapshotEntity s, SequenceFeatures seq)
    {
        var score = s.MainNetInflow switch
        {
            >= 100_000_000m => 100m,
            >= 50_000_000m => 85m,
            >= 20_000_000m => 70m,
            > 0m => 55m,
            _ => 0m
        };
        if (seq.ConsecutiveInflowDays >= 3) score += 15;
        else if (seq.ConsecutiveInflowDays >= 2) score += 8;
        return Math.Min(score, 100m);
    }

    /// <summary>多日形态：突破新高 / 缩量回踩企稳 / 阶梯放量 = 强势中继信号。</summary>
    public static decimal Form(SequenceFeatures seq)
    {
        decimal score = 50; // 中性基础
        if (seq.BreakoutNewHigh) score += 30;
        if (seq.PullbackStabilize) score += 25;
        if (seq.StairVolume) score += 15;
        return Math.Min(score, 100m);
    }

    /// <summary>题材合力：命中当日热门题材（活跃股扎堆的概念）加分；命中越多、题材越热越高。</summary>
    public static decimal Theme(string code, SelectionContext? ctx, out List<string> hitHotConcepts)
    {
        hitHotConcepts = new List<string>();
        if (ctx == null || !ctx.ConceptsByCode.TryGetValue(code, out var concepts) || concepts.Count == 0)
            return 30m; // 无概念数据：中性偏低

        var hits = concepts.Where(c => ctx.HotConcepts.ContainsKey(c)).ToList();
        if (hits.Count == 0) return 30m; // 有概念但未踩中风口

        hitHotConcepts = hits.OrderByDescending(c => ctx.HotConcepts[c]).ToList();
        var maxHeat = hits.Max(c => ctx.HotConcepts[c]);
        var score = 60m + Math.Min((hits.Count - 1) * 10m, 20m) + Math.Min(maxHeat * 3m, 20m);
        return Math.Min(score, 100m);
    }

    /// <summary>板块强弱：个股所属行业当日强度分位（0-100）。</summary>
    public static decimal Sector(string code, SelectionContext? ctx)
    {
        if (ctx == null || !ctx.IndustryByCode.TryGetValue(code, out var industry))
            return 50m;
        return ctx.IndustryStrength.TryGetValue(industry, out var st) ? st : 50m;
    }

    /// <summary>涨停性质惩罚：低位首板还有空间(轻罚)，高位/连板次日高开追高(重罚)。</summary>
    public static decimal LimitUpPenalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria)
    {
        if (seq.ConsecutiveLimitUp >= 2 && s.Rise20d > 30m) return 22m; // 高位连板
        if (seq.ConsecutiveLimitUp >= 2) return 12m;                    // 连板
        if (s.IsLimitUp && s.Rise20d > 30m) return 14m;                 // 高位首板
        if (s.IsLimitUp) return 6m;                                     // 低位首板（轻罚）
        if (s.ChangePercent > criteria.HealthyRiseMax) return 8m;       // 涨幅过大未封板
        return 0m;
    }

    /// <summary>
    /// 技术面（埋伏/低吸口径）：均线多头排列 / MACD / RSI(多头·回调·超卖反弹) / 站上均价。
    /// RSI 超买不加分。Trend 策略另有自己的技术口径（见 <see cref="TechnicalTrend"/>）。
    /// </summary>
    public static decimal Technical(DailyMarketSnapshotEntity s)
    {
        decimal score = 0;

        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0)
        {
            if (s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5) score += 30; // 完美多头排列
            else if (s.Close >= s.Ma20) score += 12;
        }
        else if (s.Ma20 > 0 && s.Close >= s.Ma20) score += 12;

        if (s.MacdGoldenCross) score += 20;
        else if (s.MacdDif > s.MacdDea) score += 10;

        score += s.Rsi switch
        {
            >= 50m and < 70m => 22m, // 多头健康
            >= 40m and < 50m => 16m, // 回调到位/蓄势
            > 0m and < 40m => 12m,   // 超卖待反弹
            _ => 0m
        };

        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) score += 10;

        return Math.Min(score, 100m);
    }

    /// <summary>
    /// 技术面（趋势口径）：偏好强势 RSI（不惩罚 70+）、均线多头发散、价在均线上方。
    /// 用于 TrendStrategy 的右侧追强。
    /// </summary>
    public static decimal TechnicalTrend(DailyMarketSnapshotEntity s)
    {
        decimal score = 0;

        // 均线多头排列是趋势的核心
        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0)
        {
            if (s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5) score += 40; // 强多头
            else if (s.Close >= s.Ma20) score += 15;
        }
        else if (s.Ma20 > 0 && s.Close >= s.Ma20) score += 15;

        if (s.MacdGoldenCross) score += 15;
        else if (s.MacdDif > s.MacdDea) score += 10;

        // 趋势口径：RSI 越强越好（强势股可长期高 RSI），不惩罚超买
        score += s.Rsi switch
        {
            >= 70m => 25m,            // 强势（趋势特征）
            >= 55m and < 70m => 20m,  // 健康多头
            >= 45m and < 55m => 10m,
            _ => 0m
        };

        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) score += 8;

        return Math.Min(score, 100m);
    }

    /// <summary>位置（低吸口径）：20 日涨幅越低（但已启动）越优，非追高。</summary>
    public static decimal Position(DailyMarketSnapshotEntity s) => s.Rise20d switch
    {
        < 0m => 40m,         // 尚未启动
        < 20m => 100m,       // 低位
        < 35m => 75m,        // 中位
        _ => 50m             // 偏高
    };

    /// <summary>位置（趋势口径）：趋势策略偏好"已经在涨"的中高位，但极端追高仍降权。</summary>
    public static decimal PositionTrend(DailyMarketSnapshotEntity s) => s.Rise20d switch
    {
        < 0m => 30m,          // 还没启动，趋势策略不喜欢
        < 15m => 60m,
        < 40m => 100m,        // 趋势确立的甜区
        < 70m => 75m,
        _ => 45m              // 过度透支
    };

    /// <summary>知名/优质席位关键词（北向、外资、头部券商总部）。</summary>
    public static readonly string[] EliteSeatKeywords =
        { "沪股通", "深股通", "QFII", "高盛", "摩根", "瑞银", "中金公司", "中信证券股份有限公司总部" };

    /// <summary>龙虎榜阵容评分：上榜 + 净买 + 买方席位质量（机构/北向/外资/头部券商）。</summary>
    public static decimal DragonTiger(DragonTigerEntity? dt)
    {
        if (dt == null) return 0;
        decimal score = 50;                       // 上榜基础
        if (dt.NetBuyAmount > 0) score += 10;     // 净买为正

        var seats = ParseSeats(dt.BuySeatsJson);
        if (seats.Count > 0)
        {
            var instCount = seats.Count(s => s.IsInstitution);
            var eliteCount = seats.Count(s => !s.IsInstitution &&
                EliteSeatKeywords.Any(k => s.SeatName.Contains(k)));
            score += Math.Min(instCount * 15, 30);
            score += Math.Min(eliteCount * 8, 16);
        }
        else if (dt.HasInstitution)
        {
            score += 25;
        }

        return Math.Min(score, 100m);
    }

    public static List<DragonTigerSeat> ParseSeats(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<DragonTigerSeat>();
        try { return JsonSerializer.Deserialize<List<DragonTigerSeat>>(json) ?? new List<DragonTigerSeat>(); }
        catch { return new List<DragonTigerSeat>(); }
    }

    public static int ToStars(decimal total) => total switch
    {
        >= 80m => 5,
        >= 65m => 4,
        >= 50m => 3,
        >= 35m => 2,
        _ => 1
    };

    public static string FormatWan(decimal yuan)
    {
        if (yuan >= 100_000_000m) return $"{yuan / 100_000_000m:F2}亿";
        return $"{yuan / 10_000m:F0}万";
    }

    /// <summary>8 因子按权重加权求和（各策略口径一致，差异只在权重与因子取值）。</summary>
    public static decimal WeightedTotal(SelectionFactorScores f, SelectionWeights w)
        => f.Capital * w.Capital + f.Technical * w.Technical + f.Position * w.Position
         + f.Form * w.Form + f.DragonTiger * w.DragonTiger + f.Activity * w.Activity
         + f.Theme * w.Theme + f.Sector * w.Sector;

    /// <summary>大盘环境综合分系数。</summary>
    public static decimal RegimeFactor(MarketRegimeLevel level, SelectionWeights w) => level switch
    {
        MarketRegimeLevel.Weak => w.RegimeWeakFactor,
        MarketRegimeLevel.Strong => w.RegimeStrongFactor,
        _ => 1.0m
    };

    /// <summary>是否"传统低弹性行业 且 大市值"应排除（如银行/电力大盘股，涨不动）——两条件同时满足才剔除。</summary>
    public static bool IsExcludedTraditionalBigCap(DailyMarketSnapshotEntity s, SelectionCriteria criteria, SelectionContext? ctx)
    {
        if (!criteria.ExcludeTraditionalIndustry || criteria.MaxTotalMarketCap <= 0) return false;
        if (s.TotalMarketCap <= criteria.MaxTotalMarketCap) return false;
        if (ctx == null || !ctx.IndustryByCode.TryGetValue(s.Code, out var ind) || string.IsNullOrEmpty(ind)) return false;
        return criteria.ExcludeIndustryKeywords.Any(k => ind.Contains(k));
    }

    /// <summary>个股是否命中当日热门题材（活跃股扎堆的概念）。</summary>
    public static bool HitsHotConcept(string code, SelectionContext? ctx)
    {
        if (ctx == null || !ctx.ConceptsByCode.TryGetValue(code, out var concepts)) return false;
        return concepts.Any(c => ctx.HotConcepts.ContainsKey(c));
    }
}
