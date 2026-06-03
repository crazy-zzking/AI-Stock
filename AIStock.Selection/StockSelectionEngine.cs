using System.Text.Json;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Narration;

namespace AIStock.Selection;

/// <summary>
/// 第二级漏斗：多因子打分引擎。在活跃池上按"主力净流入 + 技术面多头未超买 +
/// 20日涨幅非追高 + 龙虎榜阵容"加权打分，排序取 TOP-N，生成星级/标签/核心逻辑。
/// 纯计算，便于单测。
/// </summary>
public class StockSelectionEngine
{
    private readonly ILogicNarrator _narrator;

    public StockSelectionEngine(ILogicNarrator narrator) => _narrator = narrator;

    /// <summary>无多日序列时的便利重载（序列特征退化为空，仅按单日打分）。</summary>
    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        SelectionCriteria criteria)
        => Select(activePool, dragonTigerByCode,
            new Dictionary<string, SequenceFeatures>(), criteria);

    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
        IReadOnlyDictionary<string, SequenceFeatures> sequenceByCode,
        SelectionCriteria criteria,
        SelectionContext? context = null)
    {
        var results = new List<StockSelectionResult>();

        // 大盘环境调节：弱市收紧（压追高线、要求资金净流入、整体降权），强市略放宽
        var regime = context?.Regime;
        var level = regime?.Level ?? MarketRegimeLevel.Neutral;
        var maxRise20dEff = level == MarketRegimeLevel.Weak
            ? Math.Min(criteria.MaxRise20d, 30m)
            : criteria.MaxRise20d;
        var minInflowEff = level == MarketRegimeLevel.Weak
            ? Math.Max(criteria.MinMainNetInflow, 1m)   // 弱市要求主力实打实净流入（>0）
            : criteria.MinMainNetInflow;
        var w = criteria.Weights ?? new SelectionWeights();
        var scoreFactor = level switch
        {
            MarketRegimeLevel.Weak => w.RegimeWeakFactor,
            MarketRegimeLevel.Strong => w.RegimeStrongFactor,
            _ => 1.0m
        };
        var regimeNote = regime?.Description ?? string.Empty;

        foreach (var hit in activePool)
        {
            var s = hit.Snapshot;

            // —— 硬过滤（受大盘环境调节）——
            if (s.MainNetInflow < minInflowEff) continue; // 主力净流入门槛（弱市要求 >0）
            if (s.Rise20d > maxRise20dEff) continue;       // 追高排除（弱市压到 30%）
            if (s.Rsi > criteria.MaxRsi) continue;         // 超买排除

            // 排除"传统低弹性行业 且 大市值"的票（如银行/电力/高速大盘股，涨不动）——两条件同时满足才剔除
            if (criteria.ExcludeTraditionalIndustry && criteria.MaxTotalMarketCap > 0 &&
                s.TotalMarketCap > criteria.MaxTotalMarketCap &&
                context != null && context.IndustryByCode.TryGetValue(s.Code, out var industry) &&
                !string.IsNullOrEmpty(industry) &&
                criteria.ExcludeIndustryKeywords.Any(k => industry.Contains(k)))
                continue;

            dragonTigerByCode.TryGetValue(s.Code, out var dt);
            if (criteria.RequireDragonTiger && dt == null) continue;

            var seq = sequenceByCode.TryGetValue(s.Code, out var sf) ? sf : new SequenceFeatures();

            // —— 因子打分（0-100）——
            var capital = ScoreCapital(s, seq);   // 含连续净流入加成
            var technical = ScoreTechnical(s);
            var position = ScorePosition(s);
            var form = ScoreForm(seq);            // 多日形态
            var dragon = ScoreDragonTiger(dt);
            var activity = Math.Min(hit.ActivityScore, 100m);
            var theme = ScoreTheme(s.Code, context, out var hitHotConcepts);  // 题材合力
            var sector = ScoreSector(s.Code, context);                        // 板块强弱

            var total = capital * w.Capital + technical * w.Technical + position * w.Position
                        + form * w.Form + dragon * w.DragonTiger + activity * w.Activity
                        + theme * w.Theme + sector * w.Sector;

            // 涨停性质惩罚（多日）：区分低位首板（仍有空间，轻罚）与高位/连板（追高风险，重罚）
            total -= LimitUpPenalty(s, seq, criteria);
            total *= scoreFactor;                  // 大盘环境整体调节
            total = Math.Clamp(total, 0m, 100m);

            var result = new StockSelectionResult
            {
                Code = s.Code,
                Name = s.Name,
                Close = s.Close,
                ChangePercent = s.ChangePercent,
                TotalMarketCap = s.TotalMarketCap,
                Rise20d = s.Rise20d,
                PeTtm = s.PeTtm,
                MainNetInflow = s.MainNetInflow,
                ConsecutiveInflowDays = seq.ConsecutiveInflowDays,
                ConsecutiveLimitUp = seq.ConsecutiveLimitUp,
                TotalScore = Math.Round(total, 1),
                RatingStars = ToStars(total),
                Tags = BuildTags(s, dt, hit.Features, seq, hitHotConcepts, sector),
                Factors = new SelectionFactorScores
                {
                    Capital = Math.Round(capital, 1),
                    Technical = Math.Round(technical, 1),
                    Position = Math.Round(position, 1),
                    DragonTiger = Math.Round(dragon, 1),
                    Activity = Math.Round(activity, 1),
                    Form = Math.Round(form, 1),
                    Theme = Math.Round(theme, 1),
                    Sector = Math.Round(sector, 1)
                }
            };

            result.CoreLogic = _narrator.Narrate(result, new NarrationContext
            {
                Snapshot = s,
                DragonTiger = dt,
                ActivityFeatures = hit.Features
            });
            result.MarketRegime = regimeNote;

            results.Add(result);
        }

        return results
            .OrderByDescending(r => r.TotalScore)
            .Take(criteria.TopN)
            .ToList();
    }

    // 资金面：主力净流入强度 + 连续净流入加成（连续多日才是真建仓）
    private static decimal ScoreCapital(DailyMarketSnapshotEntity s, SequenceFeatures seq)
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

    // 多日形态：突破新高 / 缩量回踩企稳 / 阶梯放量 = 强势中继信号
    private static decimal ScoreForm(SequenceFeatures seq)
    {
        decimal score = 50; // 中性基础
        if (seq.BreakoutNewHigh) score += 30;
        if (seq.PullbackStabilize) score += 25;
        if (seq.StairVolume) score += 15;
        return Math.Min(score, 100m);
    }

    // 题材合力：命中当日热门题材（活跃股扎堆的概念）加分；命中越多、题材越热越高。
    private static decimal ScoreTheme(string code, SelectionContext? ctx, out List<string> hitHotConcepts)
    {
        hitHotConcepts = new List<string>();
        if (ctx == null || !ctx.ConceptsByCode.TryGetValue(code, out var concepts) || concepts.Count == 0)
            return 30m; // 无概念数据：中性偏低

        var hits = concepts.Where(c => ctx.HotConcepts.ContainsKey(c)).ToList();
        if (hits.Count == 0) return 30m; // 有概念但未踩中风口

        // 命中热门题材：基础 60，命中个数与最高热度加成
        hitHotConcepts = hits.OrderByDescending(c => ctx.HotConcepts[c]).ToList();
        var maxHeat = hits.Max(c => ctx.HotConcepts[c]);
        var score = 60m + Math.Min((hits.Count - 1) * 10m, 20m) + Math.Min(maxHeat * 3m, 20m);
        return Math.Min(score, 100m);
    }

    // 板块强弱：个股所属行业当日强度分位（0-100），强势板块的票溢价、弱势板块降权。
    private static decimal ScoreSector(string code, SelectionContext? ctx)
    {
        if (ctx == null || !ctx.IndustryByCode.TryGetValue(code, out var industry))
            return 50m;
        return ctx.IndustryStrength.TryGetValue(industry, out var st) ? st : 50m;
    }

    // 涨停性质惩罚：低位首板还有空间(轻罚)，高位/连板次日高开追高(重罚)
    private static decimal LimitUpPenalty(DailyMarketSnapshotEntity s, SequenceFeatures seq, SelectionCriteria criteria)
    {
        if (seq.ConsecutiveLimitUp >= 2 && s.Rise20d > 30m) return 22m; // 高位连板
        if (seq.ConsecutiveLimitUp >= 2) return 12m;                    // 连板
        if (s.IsLimitUp && s.Rise20d > 30m) return 14m;                 // 高位首板
        if (s.IsLimitUp) return 6m;                                     // 低位首板（轻罚）
        if (s.ChangePercent > criteria.HealthyRiseMax) return 8m;       // 涨幅过大未封板
        return 0m;
    }

    // 技术面：多种强势形态综合，MACD 金叉只是其一、不再主导。
    // 涵盖 均线多头排列 / MACD / RSI(多头·回调到位·超卖反弹) / 盘中站上均价。
    private static decimal ScoreTechnical(DailyMarketSnapshotEntity s)
    {
        decimal score = 0;

        // 1) 均线排列（最强趋势信号）
        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0)
        {
            if (s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5) score += 30; // 完美多头排列
            else if (s.Close >= s.Ma20) score += 12;                                   // 至少站上中期均线
        }
        else if (s.Ma20 > 0 && s.Close >= s.Ma20) score += 12;

        // 2) MACD：金叉/多头（普通一项，不主导）
        if (s.MacdGoldenCross) score += 20;
        else if (s.MacdDif > s.MacdDea) score += 10;

        // 3) RSI 状态：多头健康 / 回调蓄势 / 超卖待反弹；超买不加分（且已被 MaxRsi 硬过滤）
        score += s.Rsi switch
        {
            >= 50m and < 70m => 22m, // 多头健康
            >= 40m and < 50m => 16m, // 回调到位/蓄势
            > 0m and < 40m => 12m,   // 超卖待反弹
            _ => 0m
        };

        // 4) 盘中站上均价（日内多头/主力护盘）
        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) score += 10;

        return Math.Min(score, 100m);
    }

    // 位置：20 日涨幅越低（但已启动）越优，非追高
    private static decimal ScorePosition(DailyMarketSnapshotEntity s) => s.Rise20d switch
    {
        < 0m => 40m,         // 尚未启动
        < 20m => 100m,       // 低位
        < 35m => 75m,        // 中位
        _ => 50m             // 偏高（未超 MaxRise20d 才会到这）
    };

    // 知名/优质席位关键词（北向、外资、头部券商总部）。"机构专用"单独由 IsInstitution 计。
    private static readonly string[] EliteSeatKeywords =
        { "沪股通", "深股通", "QFII", "高盛", "摩根", "瑞银", "中金公司", "中信证券股份有限公司总部" };

    // 龙虎榜阵容评分：上榜 + 净买 + 买方席位质量（机构/北向/外资/头部券商）
    private static decimal ScoreDragonTiger(DragonTigerEntity? dt)
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
            score += Math.Min(instCount * 15, 30); // 机构专用席位（最多 +30）
            score += Math.Min(eliteCount * 8, 16); // 知名/北向/外资席位（最多 +16）
        }
        else if (dt.HasInstitution)
        {
            score += 25;                           // 无席位明细时退化到机构标记
        }

        return Math.Min(score, 100m);
    }

    private static List<DragonTigerSeat> ParseSeats(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<DragonTigerSeat>();
        try { return JsonSerializer.Deserialize<List<DragonTigerSeat>>(json) ?? new List<DragonTigerSeat>(); }
        catch { return new List<DragonTigerSeat>(); }
    }

    private static int ToStars(decimal total) => total switch
    {
        >= 80m => 5,
        >= 65m => 4,
        >= 50m => 3,
        >= 35m => 2,
        _ => 1
    };

    private static List<string> BuildTags(DailyMarketSnapshotEntity s, DragonTigerEntity? dt, List<string> activityFeatures,
        SequenceFeatures seq, List<string> hitHotConcepts, decimal sectorScore)
    {
        var tags = new List<string>();
        if (hitHotConcepts.Count > 0) tags.Add($"风口·{hitHotConcepts[0]}"); // 命中最热题材
        if (sectorScore >= 80m) tags.Add("强势板块");
        if (s.MainNetInflow > 0)
            tags.Add(seq.ConsecutiveInflowDays >= 2
                ? $"主力连{seq.ConsecutiveInflowDays}日+{FormatWan(s.MainNetInflow)}"
                : $"主力+{FormatWan(s.MainNetInflow)}");
        if (s.Ma5 > 0 && s.Ma10 > 0 && s.Ma20 > 0 && s.Ma5 >= s.Ma10 && s.Ma10 >= s.Ma20 && s.Close >= s.Ma5)
            tags.Add("多头排列");
        if (s.MacdGoldenCross) tags.Add("MACD刚金叉");
        if (s.Rsi is > 0m and < 40m) tags.Add("超卖反弹");
        if (s.AvgPrice > 0 && s.Close >= s.AvgPrice) tags.Add("站上均价");
        if (seq.BreakoutNewHigh) tags.Add("突破新高");
        else if (seq.PullbackStabilize) tags.Add("回踩企稳");
        if (activityFeatures.Contains("温和放量")) tags.Add("温和放量");
        else if (seq.ConsecutiveLimitUp >= 2) tags.Add($"{seq.ConsecutiveLimitUp}连板");
        else if (s.IsLimitUp) tags.Add("首板");
        if (dt != null)
        {
            var instCount = ParseSeats(dt.BuySeatsJson).Count(x => x.IsInstitution);
            tags.Add(instCount > 0 ? $"龙虎榜·机构{instCount}席"
                : dt.HasInstitution ? "龙虎榜·机构" : "龙虎榜");
        }
        return tags;
    }

    private static string FormatWan(decimal yuan)
    {
        if (yuan >= 100_000_000m) return $"{yuan / 100_000_000m:F2}亿";
        return $"{yuan / 10_000m:F0}万";
    }
}
