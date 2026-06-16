namespace AIStock.Worker;

/// <summary>
/// 情报采集配置
/// </summary>
public class IntelligenceSyncOptions
{
    public const string SectionName = "IntelligenceSync";

    /// <summary>是否启用情报采集</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动后延迟立即跑一次</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>采集周期（分钟）</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>是否采集财经新闻（HttpClient，轻量）</summary>
    public bool CollectNews { get; set; } = true;

    /// <summary>每轮采集新闻条数</summary>
    public int NewsCount { get; set; } = 20;

    /// <summary>是否采集上市公司公告</summary>
    public bool CollectAnnouncements { get; set; } = true;

    /// <summary>每轮采集公告条数</summary>
    public int AnnouncementCount { get; set; } = 50;

    /// <summary>
    /// 公告标题关键字过滤：仅当标题包含这些利好/利空字眼时才送 LLM 分析，否则跳过（省 token）。
    /// 留空则全部分析。
    /// </summary>
    public List<string> AnnouncementKeywords { get; set; } = new()
    {
        // 利好
        "中标", "中标公告", "重大合同", "签订", "订单", "收购", "重组", "资产重组", "增持", "回购",
        "业绩预增", "扭亏", "预盈", "高送转", "分红", "股权激励", "控股", "战略合作", "获批", "通过",
        "投资", "扩产", "涨价", "提价",
        // 利空
        "减持", "业绩预减", "预亏", "亏损", "商誉减值", "立案", "处罚", "问询", "退市", "*ST", "ST",
        "停牌", "质押", "诉讼", "违规", "解除", "终止",
        // 中性但重大、值得分析
        "自愿披露", "进展", "框架协议", "合作协议", "意向", "专利", "获得", "认定", "资质", "许可",
        "业绩快报", "经营数据", "月度经营", "中标候选", "设立", "增资", "定增", "非公开发行",
        "可转债", "募集资金", "重大事项", "停复牌", "控制权"
    };

    /// <summary>是否采集研报（Playwright 无头浏览器）</summary>
    public bool CollectReports { get; set; } = true;

    /// <summary>每轮采集研报条数</summary>
    public int ReportCount { get; set; } = 10;

    /// <summary>每条情报 LLM 抽取之间的节流间隔（毫秒），控制 LLM 调用频率</summary>
    public int ItemThrottleMs { get; set; } = 500;
}
