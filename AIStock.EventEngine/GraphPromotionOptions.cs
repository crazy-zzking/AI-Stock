namespace AIStock.EventEngine;

/// <summary>
/// 候选边晋升配置：候选边足够可信时晋升到权威图谱。
/// </summary>
public class GraphPromotionOptions
{
    public const string SectionName = "GraphPromotion";

    /// <summary>晋升所需最小提及次数（被多少篇情报印证）</summary>
    public int MinMentions { get; set; } = 3;

    /// <summary>晋升所需最小可信度（0-100）</summary>
    public int MinCredibility { get; set; } = 70;

    /// <summary>单次最多晋升多少条（限流）</summary>
    public int MaxPerRun { get; set; } = 200;
}
