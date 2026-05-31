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
        bool limitUp = false) =>
        new()
        {
            Code = code,
            Name = code,
            Date = new DateTime(2026, 5, 12),
            Close = close,
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
}
