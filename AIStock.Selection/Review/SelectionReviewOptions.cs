namespace AIStock.Selection.Review;

/// <summary>
/// 选股 LLM 复评配置。复评是选股之后的可选附加步骤，不影响选股主路（纯 DB）。
/// </summary>
public class SelectionReviewOptions
{
    public const string SectionName = "SelectionReview";

    /// <summary>是否启用 LLM 复评（默认关闭，配好模型再开）</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>每批复评的前 N 只（逐只调 LLM，成本主驱动）</summary>
    public int TopN { get; set; } = 20;

    /// <summary>指定复评模型 id（空 = 用 LLM 默认模型）</summary>
    public string? ModelId { get; set; }

    /// <summary>情报印证回看天数（取该股近 N 天的 event_record）</summary>
    public int IntelligenceLookbackDays { get; set; } = 7;

    /// <summary>每只股票最多带入的情报条数</summary>
    public int MaxIntelligencePerStock { get; set; } = 5;

    /// <summary>逐只调用之间的间隔（毫秒，防限流）</summary>
    public int PerCallDelayMs { get; set; } = 300;
}
