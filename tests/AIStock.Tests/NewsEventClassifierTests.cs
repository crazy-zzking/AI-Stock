using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>
/// 消息面事件分类测试：仅 news/report 参与；重雷硬否决、利空降分、优质利好(importance≥6)加分；
/// knowledge-star 不参与。
/// </summary>
public class NewsEventClassifierTests
{
    [Fact]
    public void Severe_Vetoes()
    {
        var sig = NewsEventClassifier.Classify(new[]
        {
            new NewsEvent("news", "neutral", 5, "某某公司:关于收到证监会立案告知书的公告"),
        });
        Assert.True(sig.Veto);
    }

    [Fact]
    public void KnowledgeStar_Ignored_NoVeto()
    {
        // 知识星球大V观点即便提到"立案"也不否决（观点≠事实）
        var sig = NewsEventClassifier.Classify(new[]
        {
            new NewsEvent("knowledge-star", "negative", 8, "我觉得这票要被立案了，赶紧跑"),
        });
        Assert.False(sig.Veto);
        Assert.Equal(50m, sig.Score); // 中性
    }

    [Fact]
    public void Bearish_LowersScore()
    {
        var sig = NewsEventClassifier.Classify(new[]
        {
            new NewsEvent("news", "negative", 5, "某某:控股股东减持计划公告"),
        });
        Assert.False(sig.Veto);
        Assert.True(sig.Score < 50m);
    }

    [Fact]
    public void QualityBullish_RaisesScore()
    {
        var sig = NewsEventClassifier.Classify(new[]
        {
            new NewsEvent("news", "positive", 7, "某某:中标重大工程项目"),
        });
        Assert.True(sig.Score > 50m);
        Assert.False(sig.Veto);
    }

    [Fact]
    public void Bullish_RequiresImportance()
    {
        // 利好但 importance<6 → 不算优质利好 → 中性（防软文）
        var sig = NewsEventClassifier.Classify(new[]
        {
            new NewsEvent("news", "positive", 3, "某某:中标小额订单"),
        });
        Assert.Equal(50m, sig.Score);
    }

    [Fact]
    public void NoEligibleEvents_Neutral()
    {
        var sig = NewsEventClassifier.Classify(System.Array.Empty<NewsEvent>());
        Assert.False(sig.Veto);
        Assert.Equal(50m, sig.Score);
    }
}
