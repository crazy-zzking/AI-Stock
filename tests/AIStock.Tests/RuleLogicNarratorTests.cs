using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection.Narration;

namespace AIStock.Tests;

/// <summary>
/// 规则核心逻辑叙述器测试。
/// </summary>
public class RuleLogicNarratorTests
{
    private static readonly RuleLogicNarrator Narrator = new();

    [Fact]
    public void Narrate_MacdGoldenCrossAndInflow_MentionsBoth()
    {
        var snap = new DailyMarketSnapshotEntity
        {
            Code = "300820",
            MacdGoldenCross = true,
            MacdDif = 1.13m,
            MacdDea = -0.03m,
            MainNetInflow = 79_130_000m,
            Rise20d = 20m,
            Rsi = 60m
        };
        var ctx = new NarrationContext { Snapshot = snap, ActivityFeatures = new() { "放量大涨" } };

        var text = Narrator.Narrate(new StockSelectionResult(), ctx);

        Assert.Contains("MACD", text);
        Assert.Contains("主力净流入", text);
        Assert.Contains("20日涨幅", text);
        Assert.Contains("放量大涨", text);
    }

    [Fact]
    public void Narrate_OnDragonTiger_MentionsIt()
    {
        var snap = new DailyMarketSnapshotEntity { Code = "A", Rise20d = 10m };
        var ctx = new NarrationContext
        {
            Snapshot = snap,
            DragonTiger = new DragonTigerEntity { Code = "A", NetBuyAmount = 50_000_000m, HasInstitution = true }
        };

        var text = Narrator.Narrate(new StockSelectionResult(), ctx);

        Assert.Contains("龙虎榜", text);
        Assert.Contains("机构", text);
    }

    [Fact]
    public void Narrate_LowPosition_LabelsLowLevel()
    {
        var snap = new DailyMarketSnapshotEntity { Code = "A", Rise20d = 15m };
        var ctx = new NarrationContext { Snapshot = snap };

        var text = Narrator.Narrate(new StockSelectionResult(), ctx);

        Assert.Contains("低位补涨", text);
    }
}
