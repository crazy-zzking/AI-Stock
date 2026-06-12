using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Strategies;

namespace AIStock.Tests;

/// <summary>
/// 小作文共振策略测试（纯计算）：双硬条件（小作文 + 热门题材）缺一不可；
/// 小作文条数加成影响排序；高位连板/无语境淘汰。
/// </summary>
public class WhisperThemeStrategyTests
{
    private static DailyMarketSnapshotEntity Snap(string code, decimal rsi = 60m, decimal mainNet = 30_000_000m) =>
        new()
        {
            Code = code, Name = code, Date = new DateTime(2026, 6, 11),
            Close = 11m, Ma5 = 10.8m, Ma10 = 10.4m, Ma20 = 10m,
            MainNetInflow = mainNet, Rise20d = 15m, Rsi = rsi,
            MacdGoldenCross = true, MacdDif = 1.1m, MacdDea = -0.03m,
        };

    private static ActivityScreener.ActivityHit Hit(DailyMarketSnapshotEntity s, decimal score = 55m)
        => new(s, score, new List<string>());

    private static readonly Dictionary<string, DragonTigerEntity> NoDragon = new();
    private static readonly Dictionary<string, SequenceFeatures> NoSeq = new();

    /// <summary>code 命中热门题材 + 指定条数小作文的上下文（默认中性情绪/未抽取可信度）。</summary>
    private static SelectionContext Ctx(params (string Code, int Notes, bool Hot)[] stocks)
    {
        var concepts = new Dictionary<string, List<string>>();
        var knowledge = new Dictionary<string, List<KnowledgeNote>>();
        foreach (var (code, notes, hot) in stocks)
        {
            concepts[code] = new List<string> { hot ? "人工智能" : "冷门概念" };
            if (notes > 0)
                knowledge[code] = Enumerable.Range(1, notes)
                    .Select(i => new KnowledgeNote($"小作文{i}", null, null, null)).ToList();
        }
        return new SelectionContext
        {
            ConceptsByCode = concepts,
            HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
            KnowledgeNotesByCode = knowledge,
        };
    }

    /// <summary>自定义小作文明细的上下文（单只票、命中热门题材）。</summary>
    private static SelectionContext CtxNotes(string code, params KnowledgeNote[] notes) => new()
    {
        ConceptsByCode = new Dictionary<string, List<string>> { [code] = new() { "人工智能" } },
        HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
        KnowledgeNotesByCode = new Dictionary<string, List<KnowledgeNote>> { [code] = notes.ToList() },
    };

    [Fact]
    public void RequiresBothWhisperAndHotConcept()
    {
        // A: 小作文+热门题材 ✓；B: 只有小作文；C: 只命中热门题材 → 仅 A 入选
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")), Hit(Snap("B")), Hit(Snap("C")) };
        var ctx = Ctx(("A", 2, true), ("B", 2, false), ("C", 0, true));

        var picks = new WhisperThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), ctx);

        var r = Assert.Single(picks);
        Assert.Equal("A", r.Code);
        Assert.Contains("小作文×风口", r.Tags);
    }

    [Fact]
    public void NoContext_SelectsNothing()
    {
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")) };
        var picks = new WhisperThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), null);
        Assert.Empty(picks);
    }

    [Fact]
    public void MoreNotes_RankHigher_WhenOtherFactorsEqual()
    {
        // 同等条件下 3 篇小作文 > 1 篇（条数加成）
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("ONE")), Hit(Snap("TRI")) };
        var ctx = Ctx(("ONE", 1, true), ("TRI", 3, true));

        var picks = new WhisperThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), ctx);

        Assert.Equal(2, picks.Count);
        Assert.Equal("TRI", picks[0].Code);
        Assert.True(picks[0].TotalScore > picks[1].TotalScore);
    }

    [Fact]
    public void HighConsecutiveLimitUp_IsExcluded()
    {
        // 3 连板高位不接（小作文常出现在情绪顶点）
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")) };
        var seq = new Dictionary<string, SequenceFeatures> { ["A"] = new() { ConsecutiveLimitUp = 3 } };
        var ctx = Ctx(("A", 2, true));

        var picks = new WhisperThemeStrategy().Select(pool, NoDragon, seq, new SelectionCriteria(), ctx);

        Assert.Empty(picks);
    }

    [Fact]
    public void OnlyNegativeNotes_IsExcluded()
    {
        // 仅有负面小作文 → 不构成做多依据，不入选
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")) };
        var ctx = CtxNotes("A", new KnowledgeNote("利空小作文", "negative", 4, 80));

        Assert.Empty(new WhisperThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), ctx));
    }

    [Fact]
    public void HighCredibility_OutranksLowCredibility_SameCount()
    {
        // 同为 1 篇：高可信(90) 加成 ×1.5 > 低可信(20) ×0.5
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("HI")), Hit(Snap("LO")) };
        var ctx = new SelectionContext
        {
            ConceptsByCode = new Dictionary<string, List<string>> { ["HI"] = new() { "人工智能" }, ["LO"] = new() { "人工智能" } },
            HotConcepts = new Dictionary<string, int> { ["人工智能"] = 5 },
            KnowledgeNotesByCode = new Dictionary<string, List<KnowledgeNote>>
            {
                ["HI"] = new() { new KnowledgeNote("高可信", "positive", 4, 90) },
                ["LO"] = new() { new KnowledgeNote("低可信", "positive", 4, 20) },
            },
        };

        var picks = new WhisperThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), ctx);

        Assert.Equal(2, picks.Count);
        Assert.Equal("HI", picks[0].Code);
        Assert.True(picks[0].TotalScore > picks[1].TotalScore);
    }

    [Fact]
    public void CoreLogic_MentionsWhisperAndTheme()
    {
        var pool = new List<ActivityScreener.ActivityHit> { Hit(Snap("A")) };
        var picks = new WhisperThemeStrategy().Select(pool, NoDragon, NoSeq, new SelectionCriteria(), Ctx(("A", 1, true)));

        var r = Assert.Single(picks);
        Assert.Contains("小作文", r.CoreLogic);
        Assert.Contains("人工智能", r.CoreLogic);
    }
}
