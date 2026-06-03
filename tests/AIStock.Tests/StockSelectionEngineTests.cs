using System.Text.Json;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Narration;

namespace AIStock.Tests;

/// <summary>
/// 第二级漏斗：多因子打分引擎测试（纯计算，无需 DB）。
/// </summary>
public class StockSelectionEngineTests
{
    private static StockSelectionEngine NewEngine() => new(new RuleLogicNarrator());

    private static DailyMarketSnapshotEntity Snap(
        string code, decimal mainNet = 30_000_000m, decimal rise20d = 15m,
        decimal rsi = 60m, bool macdGolden = true, decimal close = 11m, decimal ma20 = 10m,
        bool limitUp = false, decimal ma5 = 10.8m, decimal ma10 = 10.4m) =>
        new()
        {
            Code = code,
            Name = code,
            Date = new DateTime(2026, 5, 12),
            Close = close,
            Ma5 = ma5,
            Ma10 = ma10,
            Ma20 = ma20,
            MainNetInflow = mainNet,
            Rise20d = rise20d,
            Rsi = rsi,
            MacdGoldenCross = macdGolden,
            MacdDif = 1.1m,
            MacdDea = -0.03m,
            IsLimitUp = limitUp
        };

    private static ActivityScreener.ActivityHit Hit(DailyMarketSnapshotEntity s, decimal score = 55m, params string[] features)
        => new(s, score, features.ToList());

    private static readonly Dictionary<string, DragonTigerEntity> NoDragon = new();

    [Fact]
    public void Select_Overbought_IsExcluded()
    {
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", rsi: 80m)) };

        var results = NewEngine().Select(pool, NoDragon, new SelectionCriteria());

        Assert.Empty(results); // RSI 80 > MaxRsi 70
    }

    [Fact]
    public void Select_ChasingHigh_IsExcluded()
    {
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", rise20d: 60m)) };

        var results = NewEngine().Select(pool, NoDragon, new SelectionCriteria());

        Assert.Empty(results); // 20 日涨幅 60 > MaxRise20d 50
    }

    [Fact]
    public void Select_QualityStock_SelectedWithTagsAndRating()
    {
        var pool = new List<ActivityScreener.ActivityHit>
        {
            Hit(Snap("A", mainNet: 80_000_000m), 70m, "放量大涨", "资金流入")
        };

        var results = NewEngine().Select(pool, NoDragon, new SelectionCriteria());

        var r = Assert.Single(results);
        Assert.True(r.RatingStars >= 4);
        Assert.Contains(r.Tags, t => t.StartsWith("主力+"));
        Assert.Contains("MACD刚金叉", r.Tags);
        Assert.False(string.IsNullOrWhiteSpace(r.CoreLogic));
    }

    [Fact]
    public void Select_DragonTiger_BoostsScoreAndTag()
    {
        var withDt = Snap("WITH");
        var without = Snap("WITHOUT");
        var pool = new List<ActivityScreener.ActivityHit> { Hit(withDt), Hit(without) };
        var dragons = new Dictionary<string, DragonTigerEntity>
        {
            ["WITH"] = new() { Code = "WITH", Date = withDt.Date, NetBuyAmount = 50_000_000m, HasInstitution = true }
        };

        var results = NewEngine().Select(pool, dragons, new SelectionCriteria());

        var with = results.Single(r => r.Code == "WITH");
        var no = results.Single(r => r.Code == "WITHOUT");
        Assert.True(with.Factors.DragonTiger > 0);
        Assert.Equal(0, no.Factors.DragonTiger);
        Assert.True(with.TotalScore > no.TotalScore);
        Assert.Contains(with.Tags, t => t.Contains("龙虎榜"));
    }

    [Fact]
    public void Select_DragonTigerSeats_ScoresAndTagsByInstitution()
    {
        var s = Snap("A");
        var seats = new List<DragonTigerSeat>
        {
            new() { SeatName = "机构专用", BuyAmount = 2_000_000m, IsInstitution = true },
            new() { SeatName = "机构专用", BuyAmount = 1_500_000m, IsInstitution = true },
            new() { SeatName = "沪股通专用", BuyAmount = 1_000_000m, IsInstitution = false },
        };
        var dragons = new Dictionary<string, DragonTigerEntity>
        {
            ["A"] = new()
            {
                Code = "A", Date = s.Date, NetBuyAmount = 30_000_000m,
                BuySeatsJson = JsonSerializer.Serialize(seats)
            }
        };
        var pool = new List<ActivityScreener.ActivityHit> { Hit(s) };

        var results = NewEngine().Select(pool, dragons, new SelectionCriteria());

        var r = Assert.Single(results);
        // 50基础 + 10净买 + 30机构(2席×15封顶) + 8北向 = 98
        Assert.True(r.Factors.DragonTiger >= 80);
        Assert.Contains("龙虎榜·机构2席", r.Tags);
    }

    [Fact]
    public void Select_ConsecutiveInflow_ScoresHigherThanSingleDay()
    {
        // 多日：连续净流入(真建仓) 资金分应高于单日流入
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", mainNet: 30_000_000m)) };
        var consec = new Dictionary<string, SequenceFeatures> { ["A"] = new() { ConsecutiveInflowDays = 4 } };
        var single = new Dictionary<string, SequenceFeatures> { ["A"] = new() { ConsecutiveInflowDays = 1 } };

        var withConsec = NewEngine().Select(pool, NoDragon, consec, new SelectionCriteria());
        var withSingle = NewEngine().Select(pool, NoDragon, single, new SelectionCriteria());

        Assert.True(withConsec[0].Factors.Capital > withSingle[0].Factors.Capital);
        Assert.True(withConsec[0].TotalScore > withSingle[0].TotalScore);
        Assert.Contains(withConsec[0].Tags, t => t.Contains("主力连4日"));
    }

    [Fact]
    public void Select_HighLevelConsecutiveLimitUp_IsPenalizedAndTagged()
    {
        // 多日：高位连板追高重罚，标签标注连板数
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", rise20d: 40m, limitUp: true)) };
        var seq = new Dictionary<string, SequenceFeatures> { ["A"] = new() { ConsecutiveLimitUp = 3 } };

        var results = NewEngine().Select(pool, NoDragon, seq, new SelectionCriteria());

        var r = Assert.Single(results);
        Assert.Contains("3连板", r.Tags);
    }

    [Fact]
    public void Select_RespectsTopN()
    {
        var pool = new List<ActivityScreener.ActivityHit>
        {
            Hit(Snap("A")), Hit(Snap("B")), Hit(Snap("C"))
        };

        var results = NewEngine().Select(pool, NoDragon, new SelectionCriteria { TopN = 2 });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Select_RequireDragonTiger_FiltersNonListed()
    {
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")) };

        var results = NewEngine().Select(pool, NoDragon, new SelectionCriteria { RequireDragonTiger = true });

        Assert.Empty(results);
    }

    [Fact]
    public void Select_ConfigurableWeights_AffectTotalScore()
    {
        // 资金满分股：把资金权重置 0 后，综合分应明显低于默认权重（验证配置中心权重确实生效）
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A", mainNet: 100_000_000m)) };

        var withDefault = NewEngine().Select(pool, NoDragon, new SelectionCriteria());
        var zeroCapital = new SelectionCriteria();
        zeroCapital.Weights.Capital = 0m;
        var withZeroCapital = NewEngine().Select(pool, NoDragon, zeroCapital);

        Assert.True(withDefault[0].TotalScore > withZeroCapital[0].TotalScore);
    }

    [Fact]
    public void Select_DefaultWeights_MatchHistoricalConstants()
    {
        // 默认权重必须等于引擎历史硬写值（0.20/0.16/0.18/0.12/0.06/0.10/0.10/0.08），保证不配置时行为不变
        var w = new SelectionWeights();
        Assert.Equal(0.20m, w.Capital);
        Assert.Equal(0.16m, w.Technical);
        Assert.Equal(0.18m, w.Position);
        Assert.Equal(0.12m, w.Form);
        Assert.Equal(0.06m, w.DragonTiger);
        Assert.Equal(0.10m, w.Activity);
        Assert.Equal(0.10m, w.Theme);
        Assert.Equal(0.08m, w.Sector);
        Assert.Equal(0.88m, w.RegimeWeakFactor);
        Assert.Equal(1.06m, w.RegimeStrongFactor);
    }
}
