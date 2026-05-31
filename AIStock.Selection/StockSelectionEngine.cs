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
        SelectionCriteria criteria)
    {
        var results = new List<StockSelectionResult>();

        foreach (var hit in activePool)
        {
            var s = hit.Snapshot;

            // —— 硬过滤 ——
            if (s.MainNetInflow < criteria.MinMainNetInflow) continue; // 主力净流入门槛
            if (s.Rise20d > criteria.MaxRise20d) continue;             // 追高排除
            if (s.Rsi > criteria.MaxRsi) continue;                     // 超买排除

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

            var total = capital * 0.22m + technical * 0.20m + position * 0.22m
                        + form * 0.16m + dragon * 0.08m + activity * 0.12m;

            // 涨停性质惩罚（多日）：区分低位首板（仍有空间，轻罚）与高位/连板（追高风险，重罚）
            total -= LimitUpPenalty(s, seq, criteria);
            total = Math.Max(0m, total);

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
                Tags = BuildTags(s, dt, hit.Features, seq),
                Factors = new SelectionFactorScores
                {
                    Capital = Math.Round(capital, 1),
                    Technical = Math.Round(technical, 1),
                    Position = Math.Round(position, 1),
                    DragonTiger = Math.Round(dragon, 1),
                    Activity = Math.Round(activity, 1),
                    Form = Math.Round(form, 1)
                }
            };

            result.CoreLogic = _narrator.Narrate(result, new NarrationContext
            {
                Snapshot = s,
                DragonTiger = dt,
                ActivityFeatures = hit.Features
            });

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

    // 技术面：MACD 金叉 + RSI 多头未超买 + 价在 MA20 上方
    private static decimal ScoreTechnical(DailyMarketSnapshotEntity s)
    {
        decimal score = 0;
        if (s.MacdGoldenCross) score += 40;
        if (s.Rsi is >= 50 and < 70) score += 30;   // 多头未超买
        else if (s.Rsi is > 0 and < 50) score += 15; // 蓄势
        if (s.Ma20 > 0 && s.Close >= s.Ma20) score += 30; // 站上 20 日线
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

    private static List<string> BuildTags(DailyMarketSnapshotEntity s, DragonTigerEntity? dt, List<string> activityFeatures, SequenceFeatures seq)
    {
        var tags = new List<string>();
        if (s.MainNetInflow > 0)
            tags.Add(seq.ConsecutiveInflowDays >= 2
                ? $"主力连{seq.ConsecutiveInflowDays}日+{FormatWan(s.MainNetInflow)}"
                : $"主力+{FormatWan(s.MainNetInflow)}");
        if (s.MacdGoldenCross) tags.Add("MACD刚金叉");
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
