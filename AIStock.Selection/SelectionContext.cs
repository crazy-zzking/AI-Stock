using AIStock.Core.Models;

namespace AIStock.Selection;

/// <summary>
/// 选股横截面上下文 — 打分时引擎需要的"大盘 / 题材 / 板块"信息，由 Service 预先算好传入，保持引擎纯计算可单测。
/// </summary>
public class SelectionContext
{
    /// <summary>大盘环境（多指数 + 广度）</summary>
    public MarketRegime? Regime { get; set; }

    /// <summary>当日热门题材：概念名 → 活跃股数（热度）</summary>
    public IReadOnlyDictionary<string, int> HotConcepts { get; set; } = new Dictionary<string, int>();

    /// <summary>个股概念：股票代码 → 概念名列表</summary>
    public IReadOnlyDictionary<string, List<string>> ConceptsByCode { get; set; } = new Dictionary<string, List<string>>();

    /// <summary>个股概念炒作点：股票代码 → (概念名 → LLM蒸馏短语)。仅展示用，缺省为空。</summary>
    public IReadOnlyDictionary<string, Dictionary<string, string>> ConceptDigestByCode { get; set; } = new Dictionary<string, Dictionary<string, string>>();

    /// <summary>个股行业：股票代码 → 行业名</summary>
    public IReadOnlyDictionary<string, string> IndustryByCode { get; set; } = new Dictionary<string, string>();

    /// <summary>板块强度：行业名 → 当日强度分（0-100，按行业平均涨幅分位）</summary>
    public IReadOnlyDictionary<string, decimal> IndustryStrength { get; set; } = new Dictionary<string, decimal>();

    /// <summary>大盘基准近 20 日涨幅（%）：参与指数 Rise20d 均值，供个股相对强度。null=指数不可用（相对强度退化为中性）。</summary>
    public decimal? BenchmarkRise20d { get; set; }

    /// <summary>个股 K 线形态：股票代码 → 当日命中的形态（仅对活跃池股票计算，按需填充）。</summary>
    public IReadOnlyDictionary<string, CandlePatternFeatures> PatternsByCode { get; set; }
        = new Dictionary<string, CandlePatternFeatures>();

    /// <summary>个股消息面信号：股票代码 → {是否重雷, 消息面分}。来自近 N 日 news/report 事件分类。无事件=中性。</summary>
    public IReadOnlyDictionary<string, NewsSignal> NewsByCode { get; set; } = new Dictionary<string, NewsSignal>();

    /// <summary>
    /// 个股关联的知识星球"小作文"（标题 + LLM 抽取的情绪/重要性/可信度）。
    /// 展示与 whisper 策略共用：负面/低可信小作文由策略侧降权或剔除。
    /// </summary>
    public IReadOnlyDictionary<string, List<KnowledgeNote>> KnowledgeNotesByCode { get; set; }
        = new Dictionary<string, List<KnowledgeNote>>();
}

/// <summary>
/// 知识星球"小作文"条目（字段来自情报事件抽取管线，零额外 LLM 成本）。
/// </summary>
/// <param name="Title">标题。</param>
/// <param name="Sentiment">情绪（positive/negative/neutral，可能为 null=未抽取）。</param>
/// <param name="Importance">重要性 1-5（null=未抽取）。</param>
/// <param name="Credibility">可信度 0-100（null=未抽取）。</param>
public readonly record struct KnowledgeNote(string Title, string? Sentiment, int? Importance, int? Credibility)
{
    /// <summary>明确负面（利空小作文不构成做多依据）。</summary>
    public bool IsNegative => string.Equals(Sentiment, "negative", StringComparison.OrdinalIgnoreCase);

    /// <summary>加成权重：高可信(≥70)×1.5、低可信(&lt;40)×0.5、其余/未抽取×1。</summary>
    public decimal BonusWeight => Credibility switch
    {
        >= 70 => 1.5m,
        < 40 => 0.5m,
        _ => 1m,
    };
}
