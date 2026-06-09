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

    /// <summary>个股关联的知识星球"小作文"标题（仅展示提示，不参与排雷/打分）。</summary>
    public IReadOnlyDictionary<string, List<string>> KnowledgeNotesByCode { get; set; } = new Dictionary<string, List<string>>();
}
