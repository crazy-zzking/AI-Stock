using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>题材生命周期分期（纯计算）：发酵/高潮/退潮/沉寂 + 退潮剔除。</summary>
public class ConceptLifecycleTests
{
    [Theory]
    [InlineData(new[] { 0, 0, 0, 1 }, ConceptStage.Emerging)]       // 刚冒头
    [InlineData(new[] { 0, 1, 2, 3 }, ConceptStage.Emerging)]       // 热度爬升（峰值即当日但不足高潮线）
    [InlineData(new[] { 1, 3, 5, 8 }, ConceptStage.Climax)]         // 当日 8 家=峰值且≥5 → 高潮
    [InlineData(new[] { 2, 8, 5, 2 }, ConceptStage.Fading)]         // 峰值 8 → 当日 2 ≤ 8×0.4 → 退潮
    [InlineData(new[] { 0, 3, 1, 0 }, ConceptStage.Fading)]         // 小高潮后归零 → 退潮
    [InlineData(new[] { 0, 1, 0, 0 }, ConceptStage.Quiet)]          // 峰值≤1 → 沉寂
    [InlineData(new[] { 5, 5, 5, 5 }, ConceptStage.Climax)]         // 持续高热=持续高潮
    [InlineData(new[] { 0, 2, 2, 1 }, ConceptStage.Emerging)]       // 峰值2(<3) 不构成退潮，仍算发酵
    public void Classify_Stages(int[] series, ConceptStage expected)
        => Assert.Equal(expected, ConceptLifecycle.Classify(series));

    [Fact]
    public void ComputeStages_CountsPerConceptPerDay()
    {
        var days = new List<IReadOnlyCollection<string>>
        {
            new HashSet<string> { "S1", "S2", "S3" },   // day1
            new HashSet<string> { "S3" },               // day2(今天)
        };
        var concepts = new Dictionary<string, List<string>>
        {
            ["S1"] = new() { "A" }, ["S2"] = new() { "A" }, ["S3"] = new() { "B" },
        };

        var stages = ConceptLifecycle.ComputeStages(days, concepts);

        Assert.Equal(ConceptStage.Quiet, stages["A"]);    // 序列 [2,0]：峰值2<3 不构成退潮，今日 0 → 沉寂
        Assert.Equal(ConceptStage.Emerging, stages["B"]); // 序列 [1,1]
    }

    [Fact]
    public void RemoveFading_RemovesOnlyFadingConcepts()
    {
        var hot = new Dictionary<string, int> { ["退潮题材"] = 6, ["发酵题材"] = 4 };
        var stages = new Dictionary<string, ConceptStage>
        {
            ["退潮题材"] = ConceptStage.Fading,
            ["发酵题材"] = ConceptStage.Emerging,
        };

        var removed = ConceptLifecycle.RemoveFading(hot, stages);

        Assert.Equal(new[] { "退潮题材" }, removed);
        Assert.False(hot.ContainsKey("退潮题材"));
        Assert.True(hot.ContainsKey("发酵题材"));
    }
}
