using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Backtest;

namespace AIStock.Tests;

/// <summary>
/// 当日热门题材识别测试：集中度加权(活跃数²/成分数) + 宽筐黑名单 + 成分护栏 + TopN。
/// </summary>
public class HotConceptsTests
{
    // 活跃股（涨幅≥5% 即算活跃）
    private static DailyMarketSnapshotEntity Active(string code)
        => new() { Code = code, Name = code, ChangePercent = 6m };

    /// <summary>构造：concept→成分数 size，其中 activeN 只活跃。</summary>
    private static void AddConcept(
        Dictionary<string, List<string>> concepts, List<DailyMarketSnapshotEntity> shots,
        string concept, int size, int activeN, string prefix)
    {
        for (int i = 1; i <= size; i++)
        {
            var code = $"{prefix}{i}";
            if (!concepts.TryGetValue(code, out var list)) concepts[code] = list = new();
            list.Add(concept);
            if (i <= activeN) shots.Add(Active(code));
        }
    }

    [Fact]
    public void Concentration_FocusedConcept_OutranksBroad()
    {
        var concepts = new Dictionary<string, List<string>>();
        var shots = new List<DailyMarketSnapshotEntity>();
        AddConcept(concepts, shots, "FOCUS", size: 5, activeN: 3, "F");   // 3²/5 = 1.8
        AddConcept(concepts, shots, "BROAD", size: 100, activeN: 4, "B"); // 4²/100 = 0.16

        var hot = SelectionContextBuilder.ComputeHotConcepts(shots, concepts);

        Assert.Contains("FOCUS", hot.Keys);
        Assert.Contains("BROAD", hot.Keys);
        Assert.Equal("FOCUS", hot.Keys.First());   // 集中度高者排前
        Assert.Equal(3, hot["FOCUS"]);             // 值=活跃股数
        Assert.Equal(4, hot["BROAD"]);
    }

    [Fact]
    public void Blocklist_PseudoConcept_IsExcluded()
    {
        var concepts = new Dictionary<string, List<string>>();
        var shots = new List<DailyMarketSnapshotEntity>();
        AddConcept(concepts, shots, "融资融券", size: 20, activeN: 8, "X"); // 宽筐，命中黑名单
        AddConcept(concepts, shots, "机器人概念", size: 10, activeN: 3, "R");

        var hot = SelectionContextBuilder.ComputeHotConcepts(shots, concepts);

        Assert.DoesNotContain("融资融券", hot.Keys);
        Assert.Contains("机器人概念", hot.Keys);
    }

    [Fact]
    public void TooSmallConcept_IsExcluded()
    {
        var concepts = new Dictionary<string, List<string>>();
        var shots = new List<DailyMarketSnapshotEntity>();
        AddConcept(concepts, shots, "TINY", size: 3, activeN: 2, "T");  // 成分<5，护栏剔除

        var hot = SelectionContextBuilder.ComputeHotConcepts(shots, concepts);

        Assert.DoesNotContain("TINY", hot.Keys);
    }

    [Fact]
    public void OneActive_NotEnough()
    {
        var concepts = new Dictionary<string, List<string>>();
        var shots = new List<DailyMarketSnapshotEntity>();
        AddConcept(concepts, shots, "SOLO", size: 10, activeN: 1, "S");  // 仅 1 只活跃

        var hot = SelectionContextBuilder.ComputeHotConcepts(shots, concepts);

        Assert.DoesNotContain("SOLO", hot.Keys);
    }

    [Fact]
    public void TopN_Caps_Result()
    {
        var concepts = new Dictionary<string, List<string>>();
        var shots = new List<DailyMarketSnapshotEntity>();
        AddConcept(concepts, shots, "A", size: 5, activeN: 4, "A");
        AddConcept(concepts, shots, "B", size: 6, activeN: 3, "B");
        AddConcept(concepts, shots, "C", size: 8, activeN: 2, "C");

        var hot = SelectionContextBuilder.ComputeHotConcepts(shots, concepts, topN: 1);

        Assert.Single(hot);
        Assert.Equal("A", hot.Keys.First()); // 4²/5=3.2 最高
    }
}
