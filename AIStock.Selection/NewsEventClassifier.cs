namespace AIStock.Selection;

/// <summary>消息面事件（喂给分类器的最小信息，解耦实体，便于单测）。</summary>
public readonly record struct NewsEvent(string? EventType, string? Sentiment, int? Importance, string? Title);

/// <summary>个股消息面信号：是否重雷(硬否决) + 消息面分(0-100，50中性)。</summary>
public readonly record struct NewsSignal(bool Veto, decimal Score)
{
    public static readonly NewsSignal Neutral = new(false, 50m);
}

/// <summary>
/// 消息面事件分类（纯函数）。<b>只认 news / report 类型</b>（公告/新闻/研报为事实源）；
/// knowledge-star（知识星球观点）不参与分类与排雷。分三档：
/// 重雷→硬否决；一般利空→降分；优质利好(importance≥阈值)→加分。
/// 关键词为内置默认，后续可迁到配置。
/// </summary>
public static class NewsEventClassifier
{
    /// <summary>重雷：命中即硬否决（不论情绪标注，监管/退市类几乎必坏）。</summary>
    private static readonly string[] SevereKeywords =
    {
        "立案", "处罚", "退市风险", "退市", "财务造假", "重大违规", "问询函", "警示函", "监管措施", "涉嫌违规",
    };

    /// <summary>一般利空：降分（频繁但不一定致命）。</summary>
    private static readonly string[] BearishKeywords =
    {
        "解禁", "限售股上市", "减持", "质押", "对外担保", "担保", "诉讼", "仲裁",
        "业绩预减", "业绩预亏", "商誉减值", "异常波动", "终止", "停牌核查",
    };

    /// <summary>优质利好：加分（需 importance 达标 + 命中催化词，过滤软文）。</summary>
    private static readonly string[] BullishKeywords =
    {
        "中标", "中标公告", "大额订单", "签订", "增持", "回购", "重组", "过会", "获批",
        "业绩预增", "业绩大增", "纳入", "指数样本", "政策", "补贴", "扩产", "提价",
    };

    private const int BullishMinImportance = 6;
    private const decimal BullishStep = 12m;   // 单条优质利好加分
    private const decimal BearishStep = 15m;    // 单条一般利空降分（略强于利好）

    private static bool Hits(string? title, string[] kws)
        => !string.IsNullOrEmpty(title) && kws.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static bool IsEligibleType(string? type)
        => string.Equals(type, "news", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "report", StringComparison.OrdinalIgnoreCase);

    /// <summary>聚合一只股票近期的 news/report 事件 → 消息面信号。</summary>
    public static NewsSignal Classify(IEnumerable<NewsEvent> events)
    {
        var veto = false;
        var bullish = 0;
        var bearish = 0;

        foreach (var e in events)
        {
            if (!IsEligibleType(e.EventType)) continue; // knowledge-star 等不参与

            if (Hits(e.Title, SevereKeywords)) { veto = true; bearish++; continue; }

            var isBearish = string.Equals(e.Sentiment, "negative", StringComparison.OrdinalIgnoreCase)
                            || Hits(e.Title, BearishKeywords);
            if (isBearish) { bearish++; continue; }

            var isBullish = string.Equals(e.Sentiment, "positive", StringComparison.OrdinalIgnoreCase)
                            && (e.Importance ?? 0) >= BullishMinImportance
                            && Hits(e.Title, BullishKeywords);
            if (isBullish) bullish++;
        }

        if (bullish == 0 && bearish == 0 && !veto) return NewsSignal.Neutral;

        var score = Math.Clamp(50m + bullish * BullishStep - bearish * BearishStep, 0m, 100m);
        return new NewsSignal(veto, score);
    }
}
