namespace AIStock.Selection;

/// <summary>题材生命周期阶段。</summary>
public enum ConceptStage
{
    /// <summary>沉寂：近窗口几乎无涨停。</summary>
    Quiet,
    /// <summary>发酵：有涨停且热度未到顶（进场窗口）。</summary>
    Emerging,
    /// <summary>高潮：当日热度即窗口峰值且足够拥挤（追高风险大）。</summary>
    Climax,
    /// <summary>退潮：曾有规模性涨停、当日热度从峰值大幅回落（题材级别的"少出手"信号）。</summary>
    Fading,
}

/// <summary>
/// 题材生命周期（纯函数）：按概念近 N 日涨停家数序列分期。
/// 核心用途是"退潮识别"——题材策略的胜负手在进场于发酵期还是退潮期，
/// 退潮题材从当日热门题材中剔除（实盘与回放共用此口径），是 RegimeGate 思想在题材维度的复用。
/// </summary>
public static class ConceptLifecycle
{
    /// <summary>生命周期判断回看窗口（交易日）。</summary>
    public const int WindowDays = 10;

    /// <summary>退潮判定：窗口峰值 ≥ 此涨停家数才算"炒作过"（峰值太小无所谓退潮）。</summary>
    private const int FadingMinPeak = 3;

    /// <summary>退潮判定：当日热度 ≤ 峰值 × 此比例视为大幅回落。</summary>
    private const decimal FadingRatio = 0.4m;

    /// <summary>高潮判定：当日涨停家数达到此值且即窗口峰值。</summary>
    private const int ClimaxMinCount = 5;

    /// <summary>按近 N 日涨停家数序列（升序，末位=当日）分期。当日有涨停即至少算发酵（早发现是生命周期的价值）。</summary>
    public static ConceptStage Classify(IReadOnlyList<int> dailyLimitUpsAsc)
    {
        if (dailyLimitUpsAsc.Count == 0) return ConceptStage.Quiet;
        var today = dailyLimitUpsAsc[^1];
        var peak = dailyLimitUpsAsc.Max();

        if (peak >= FadingMinPeak && today <= peak * FadingRatio) return ConceptStage.Fading;
        if (today >= ClimaxMinCount && today >= peak) return ConceptStage.Climax;
        return today >= 1 ? ConceptStage.Emerging : ConceptStage.Quiet;
    }

    /// <summary>
    /// 从近 N 日"每日涨停代码清单"（升序，末位=当日）计算各概念的生命周期阶段。
    /// 只返回窗口内出现过涨停的概念（其余视为 Quiet）。
    /// </summary>
    public static Dictionary<string, ConceptStage> ComputeStages(
        IReadOnlyList<IReadOnlyCollection<string>> limitUpCodesByDayAsc,
        IReadOnlyDictionary<string, List<string>> conceptsByCode)
    {
        var days = limitUpCodesByDayAsc.Count;
        var countsByConcept = new Dictionary<string, int[]>();

        for (var d = 0; d < days; d++)
        {
            foreach (var code in limitUpCodesByDayAsc[d])
            {
                if (!conceptsByCode.TryGetValue(code, out var concepts)) continue;
                foreach (var c in concepts)
                {
                    if (!countsByConcept.TryGetValue(c, out var arr)) countsByConcept[c] = arr = new int[days];
                    arr[d]++;
                }
            }
        }

        return countsByConcept.ToDictionary(kv => kv.Key, kv => Classify(kv.Value));
    }

    /// <summary>把退潮（Fading）题材从热门题材中剔除，返回被剔除的概念名（供日志/展示）。</summary>
    public static List<string> RemoveFading(
        Dictionary<string, int> hotConcepts, IReadOnlyDictionary<string, ConceptStage> stages)
    {
        var removed = hotConcepts.Keys
            .Where(c => stages.TryGetValue(c, out var st) && st == ConceptStage.Fading)
            .ToList();
        foreach (var c in removed) hotConcepts.Remove(c);
        return removed;
    }
}
