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

    public List<StockSelectionResult> Select(
        IReadOnlyList<ActivityScreener.ActivityHit> activePool,
        IReadOnlyDictionary<string, DragonTigerEntity> dragonTigerByCode,
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

            // —— 因子打分（0-100）——
            var capital = ScoreCapital(s);
            var technical = ScoreTechnical(s);
            var position = ScorePosition(s);
            var dragon = ScoreDragonTiger(dt);
            var activity = Math.Min(hit.ActivityScore, 100m);

            var total = capital * 0.30m + technical * 0.30m + position * 0.20m
                        + dragon * 0.10m + activity * 0.10m;

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
                TotalScore = Math.Round(total, 1),
                RatingStars = ToStars(total),
                Tags = BuildTags(s, dt, hit.Features),
                Factors = new SelectionFactorScores
                {
                    Capital = Math.Round(capital, 1),
                    Technical = Math.Round(technical, 1),
                    Position = Math.Round(position, 1),
                    DragonTiger = Math.Round(dragon, 1),
                    Activity = Math.Round(activity, 1)
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

    // 资金面：主力净流入强度
    private static decimal ScoreCapital(DailyMarketSnapshotEntity s) => s.MainNetInflow switch
    {
        >= 100_000_000m => 100m,
        >= 50_000_000m => 85m,
        >= 20_000_000m => 70m,
        > 0m => 55m,
        _ => 0m
    };

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

    private static List<string> BuildTags(DailyMarketSnapshotEntity s, DragonTigerEntity? dt, List<string> activityFeatures)
    {
        var tags = new List<string>();
        if (s.MainNetInflow > 0) tags.Add($"主力+{FormatWan(s.MainNetInflow)}");
        if (s.MacdGoldenCross) tags.Add("MACD刚金叉");
        if (s.IsLimitUp) tags.Add("涨停");
        else if (activityFeatures.Contains("放量大涨")) tags.Add("放量大涨");
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
