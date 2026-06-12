using AIStock.Core.Models;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>
/// 出手闸门口径测试（纯计算）：验证弱市压缩 TopN + 分数下限，风险释放更严，
/// 中性/强市保持原行为（不限数量、仅全局 MinScore 下限）。
/// </summary>
public class RegimeGateTests
{
    /// <summary>构造一批分数递减的候选：100,90,80,...（n 只）。</summary>
    private static List<StockSelectionResult> Candidates(int n)
        => Enumerable.Range(0, n)
            .Select(i => new StockSelectionResult { Code = $"C{i}", TotalScore = 100m - i * 10m })
            .ToList();

    private static SelectionCriteria Crit(int topN = 20, SelectionWeights? w = null)
        => new() { TopN = topN, Weights = w ?? new SelectionWeights() };

    [Fact]
    public void Neutral_NoCap_NoFloor_KeepsTopN()
    {
        // 中性市：默认 MinScore=0、不压缩 → 取满 TopN
        var picks = RegimeGate.Apply(Candidates(8), Crit(topN: 5),
            MarketRegimeLevel.Neutral, RegimeKind.Range);

        Assert.Equal(5, picks.Count);
        Assert.Equal("C0", picks[0].Code); // 最高分在前（输入已降序）
    }

    [Fact]
    public void Weak_CapsTopN_AndAppliesFloor()
    {
        // 弱市：默认上限 3、下限 50。候选 100,90,80,70,60,50,40,30 → 下限过滤掉 <50，再取前 3
        var picks = RegimeGate.Apply(Candidates(8), Crit(topN: 20),
            MarketRegimeLevel.Weak, RegimeKind.Range);

        Assert.Equal(3, picks.Count);                 // 被弱市上限压到 3
        Assert.All(picks, p => Assert.True(p.TotalScore >= 50m));
    }

    [Fact]
    public void Weak_FloorRemovesLowScores_EvenUnderCap()
    {
        // 弱市下限 50：若高分不足，数量自然少于上限（宁缺毋滥）
        var weak = new SelectionWeights { RegimeWeakMinScore = 85m, RegimeWeakTopNCap = 3 };
        var picks = RegimeGate.Apply(Candidates(8), Crit(topN: 20, w: weak),
            MarketRegimeLevel.Weak, RegimeKind.Range);

        Assert.Equal(2, picks.Count);                 // 仅 100、90 过线
    }

    [Fact]
    public void RiskOff_OverridesWeak_OnlyOneSurvives()
    {
        // 风险释放优先于弱市：默认上限 1、下限 55
        var picks = RegimeGate.Apply(Candidates(8), Crit(topN: 20),
            MarketRegimeLevel.Weak, RegimeKind.RiskOff);

        var only = Assert.Single(picks);
        Assert.Equal("C0", only.Code);
        Assert.True(only.TotalScore >= 55m);
    }

    [Fact]
    public void RiskOff_ZeroCap_HaltsCompletely()
    {
        // 配置上限 0 → 风险释放日完全空仓
        var w = new SelectionWeights { RegimeRiskOffTopNCap = 0 };
        var picks = RegimeGate.Apply(Candidates(8), Crit(topN: 20, w: w),
            MarketRegimeLevel.Weak, RegimeKind.RiskOff);

        Assert.Empty(picks);
    }

    [Fact]
    public void Strong_AppliesGlobalMinScoreOnly()
    {
        // 强市：不压缩数量，但全局 MinScore 仍生效
        var w = new SelectionWeights { MinScore = 75m };
        var picks = RegimeGate.Apply(Candidates(8), Crit(topN: 20, w: w),
            MarketRegimeLevel.Strong, RegimeKind.TrendUp);

        Assert.Equal(3, picks.Count);                 // 100、90、80 过线
        Assert.All(picks, p => Assert.True(p.TotalScore >= 75m));
    }
}
