namespace AIStock.Core.Models;

/// <summary>
/// 选股条件（两级漏斗参数 + 多因子阈值）
/// </summary>
public class SelectionCriteria
{
    /// <summary>返回 TOP-N</summary>
    public int TopN { get; set; } = 5;

    // —— 第一级：活跃度粗筛（埋伏型：偏好温和放量未涨停，规避次日高开追高）——
    /// <summary>温和放量上涨：涨幅下限（%），已启动</summary>
    public decimal HealthyRiseMin { get; set; } = 2m;
    /// <summary>温和放量上涨：涨幅上限（%）= 追高线，超过视为追高/次日高开风险</summary>
    public decimal HealthyRiseMax { get; set; } = 7m;
    /// <summary>放量/震荡：量比下限</summary>
    public decimal MinVolumeRatio { get; set; } = 1.5m;
    /// <summary>放量震荡：振幅下限（%）</summary>
    public decimal ShockAmplitude { get; set; } = 6m;

    // —— 第二级：多因子阈值 ——
    /// <summary>主力净流入下限（元），低于则不入选</summary>
    public decimal MinMainNetInflow { get; set; } = 0m;
    /// <summary>RSI 超买线，超过视为过热（降权/排除）</summary>
    public decimal MaxRsi { get; set; } = 70m;
    /// <summary>20 日涨幅上限（%），超过视为追高（排除）</summary>
    public decimal MaxRise20d { get; set; } = 50m;
    /// <summary>是否要求当日上龙虎榜</summary>
    public bool RequireDragonTiger { get; set; } = false;

    /// <summary>排除"传统大盘股"的市值阈值（元）：仅当个股属传统行业 且 市值超过此值时才排除。0 = 不限。默认 500 亿</summary>
    public decimal MaxTotalMarketCap { get; set; } = 50_000_000_000m;
    /// <summary>是否排除"传统低弹性行业 且 大市值"的票（两条件同时满足才剔除）</summary>
    public bool ExcludeTraditionalIndustry { get; set; } = true;
    /// <summary>传统行业关键字：个股所属行业(stock_base.Industry)命中任一才算"传统行业"</summary>
    public List<string> ExcludeIndustryKeywords { get; set; } = new()
    { "金融", "银行", "保险", "证券", "房地产", "采矿", "建筑业", "交通运输", "石油", "钢铁", "电力", "燃气" };

    /// <summary>是否启用 LLM 生成核心逻辑（默认规则模板）</summary>
    public bool UseLlmNarrative { get; set; } = false;

    /// <summary>是否启用"重雷硬否决"（命中立案/处罚/退市/问询函等事件的票直接剔除）。默认开。</summary>
    public bool EnableNewsVeto { get; set; } = true;
    /// <summary>消息面事件回看交易日数（取候选股近 N 日的 news/report 事件）。</summary>
    public int NewsLookbackDays { get; set; } = 5;

    /// <summary>
    /// 运行时选择的 K 线形态键（取值见 CandlePatternAnalyzer.PatternKeys）。
    /// 仅对启用形态过滤的策略（如 kpattern）生效：非空时覆盖策略定义里的形态列表；空=用策略默认（全部形态）。
    /// </summary>
    public List<string> Patterns { get; set; } = new();

    /// <summary>形态过滤是否要求全部命中（默认 false=命中任一即可）。</summary>
    public bool RequireAllPatterns { get; set; } = false;

    /// <summary>多因子打分权重 + 大盘环境系数（默认值即引擎历史硬写值，配置中心可覆盖）</summary>
    public SelectionWeights Weights { get; set; } = new();
}

/// <summary>
/// 多因子打分权重与大盘环境系数。默认值与引擎历史硬写常量一致，保证不配置时行为不变。
/// 由配置中心（selection_config）按版本下发；引擎按"绝对权重加权"，各权重之和不要求等于 1。
/// </summary>
public class SelectionWeights
{
    /// <summary>资金面权重（主力净流入 + 连续净流入）</summary>
    public decimal Capital { get; set; } = 0.20m;
    /// <summary>技术面权重（均线/MACD/RSI/均价）</summary>
    public decimal Technical { get; set; } = 0.16m;
    /// <summary>位置权重（20 日涨幅，非追高）</summary>
    public decimal Position { get; set; } = 0.18m;
    /// <summary>形态权重（多日序列：突破/回踩/阶梯放量）</summary>
    public decimal Form { get; set; } = 0.12m;
    /// <summary>龙虎榜权重</summary>
    public decimal DragonTiger { get; set; } = 0.06m;
    /// <summary>活跃度权重（一级粗筛分）</summary>
    public decimal Activity { get; set; } = 0.10m;
    /// <summary>题材权重（命中当日热门题材合力）</summary>
    public decimal Theme { get; set; } = 0.10m;
    /// <summary>板块权重（所属行业当日强弱）</summary>
    public decimal Sector { get; set; } = 0.08m;

    // —— 扩展因子（默认权重 0：不配置即不参与，保持历史行为不变）——
    /// <summary>波动/风险权重（近 5 日平均振幅，低波动=稳健=高分）。默认 0 不启用。</summary>
    public decimal Volatility { get; set; } = 0m;
    /// <summary>相对强度权重（个股 20 日涨幅相对大盘基准的超额）。默认 0 不启用。</summary>
    public decimal RelativeStrength { get; set; } = 0m;

    /// <summary>消息面权重（news/report 事件利好/利空）。默认 0 不启用（先观察，回测无法验证）。</summary>
    public decimal News { get; set; } = 0m;

    /// <summary>弱市综合分系数（&lt;1 收紧）</summary>
    public decimal RegimeWeakFactor { get; set; } = 0.88m;
    /// <summary>强市综合分系数（&gt;1 放宽）</summary>
    public decimal RegimeStrongFactor { get; set; } = 1.06m;

    // —— 出手闸门（让大盘环境真正控制"出不出手、出几只"，而非只给分数打折）——
    /// <summary>全局综合分下限：低于此分一律不入选（0 = 不限，仅对中性/强市生效，弱市/风险释放另有更严下限）。</summary>
    public decimal MinScore { get; set; } = 0m;
    /// <summary>弱市综合分下限：弱市里低于此分不入选。</summary>
    public decimal RegimeWeakMinScore { get; set; } = 50m;
    /// <summary>弱市最多出手只数（压缩 TopN，宁缺毋滥）。</summary>
    public int RegimeWeakTopNCap { get; set; } = 3;
    /// <summary>风险释放（跌停扩散/情绪退潮）综合分下限：更严。</summary>
    public decimal RegimeRiskOffMinScore { get; set; } = 55m;
    /// <summary>风险释放最多出手只数（近乎空仓；设 0 则风险释放日完全空仓）。</summary>
    public int RegimeRiskOffTopNCap { get; set; } = 1;

    // —— 吸筹埋伏（ambush）硬触发阈值（默认保持原行为，供调参实验）——
    /// <summary>吸筹埋伏：近 10 日最少涨停次数（启动背景强度，越大要求启动越强）。</summary>
    public int AmbushMinLimitUpIn10 { get; set; } = 1;
    /// <summary>吸筹埋伏：是否要求当日主力净流入≥0（洗盘期仍有资金承接，过滤出货阴跌）。</summary>
    public bool AmbushRequireInflow { get; set; }
    /// <summary>吸筹埋伏：是否要求命中当日热门题材（板块有炒作预期，埋伏才有人来抬轿）。</summary>
    public bool AmbushRequireHotConcept { get; set; }
}

/// <summary>大盘环境等级（用于选股松紧系数）</summary>
public enum MarketRegimeLevel { Weak, Neutral, Strong }

/// <summary>市场状态类型（决定推荐策略）</summary>
public enum RegimeKind
{
    /// <summary>震荡市：指数横盘、热点轮动 → 低吸</summary>
    Range,
    /// <summary>趋势上涨：指数走强、普涨扩散 → 趋势跟随</summary>
    TrendUp,
    /// <summary>题材主导：涨停潮、结构性活跃 → 题材龙头</summary>
    ThemeMarket,
    /// <summary>风险释放：跌停扩散、情绪退潮 → 防守/低吸</summary>
    RiskOff,
}

/// <summary>单个指数行情快照</summary>
public class IndexQuote
{
    public string Name { get; set; } = string.Empty;
    /// <summary>当日涨跌幅（%）</summary>
    public decimal ChangePercent { get; set; }
    /// <summary>是否站上 20 日均线</summary>
    public bool AboveMa20 { get; set; }
    /// <summary>近 20 日涨幅（%），供个股相对强度基准</summary>
    public decimal Rise20d { get; set; }
}

/// <summary>
/// 大盘环境判断 — 综合多个主要指数（上证/深成/创业板/沪深300 的涨跌幅与是否站上20日线）
/// 与全市场涨跌广度，用于调节选股松紧。
/// </summary>
public class MarketRegime
{
    public MarketRegimeLevel Level { get; set; } = MarketRegimeLevel.Neutral;
    /// <summary>市场状态类型（决定推荐策略）</summary>
    public RegimeKind Kind { get; set; } = RegimeKind.Range;
    /// <summary>推荐策略键（lowdip/trend/theme）</summary>
    public string RecommendedStrategy { get; set; } = "lowdip";
    /// <summary>参与判断的各指数快照</summary>
    public List<IndexQuote> Indices { get; set; } = new();
    /// <summary>是否取到至少一个指数（取不到则仅用广度判断）</summary>
    public bool HasIndex => Indices.Count > 0;
    /// <summary>全市场上涨家数占比（0-1）</summary>
    public decimal AdvanceRatio { get; set; }
    /// <summary>当日涨停家数</summary>
    public int LimitUpCount { get; set; }
    /// <summary>当日跌停家数（近似：跌幅≤-9.8%）</summary>
    public int LimitDownCount { get; set; }
    /// <summary>文字描述</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 各因子得分明细（0-100）
/// </summary>
public class SelectionFactorScores
{
    /// <summary>资金面（主力净流入）</summary>
    public decimal Capital { get; set; }
    /// <summary>技术面（MACD金叉/RSI未超买/均线多头）</summary>
    public decimal Technical { get; set; }
    /// <summary>位置（20日涨幅，非追高）</summary>
    public decimal Position { get; set; }
    /// <summary>龙虎榜</summary>
    public decimal DragonTiger { get; set; }
    /// <summary>活跃度（第一级粗筛得分）</summary>
    public decimal Activity { get; set; }
    /// <summary>形态（多日序列：阶梯放量/突破/回踩企稳）</summary>
    public decimal Form { get; set; }
    /// <summary>题材（命中当日热门题材/风口的合力）</summary>
    public decimal Theme { get; set; }
    /// <summary>板块（所属行业当日强弱：板块联动/资金共识）</summary>
    public decimal Sector { get; set; }
    /// <summary>波动/风险（近 5 日平均振幅，低波动=稳健=高分）</summary>
    public decimal Volatility { get; set; }
    /// <summary>相对强度（个股 20 日涨幅相对大盘基准的超额）</summary>
    public decimal RelativeStrength { get; set; }

    /// <summary>消息面（news/report 事件：利好加分 / 利空降分，50 中性）。knowledge-star 不计入。</summary>
    public decimal News { get; set; }
}

/// <summary>
/// 选股历史记录元信息（列表展示用，不含明细）
/// </summary>
public class SelectionHistoryItem
{
    public long Id { get; set; }
    /// <summary>选股所基于的交易日</summary>
    public DateTime TradingDate { get; set; }
    /// <summary>选股时间（UTC）</summary>
    public DateTime RunAt { get; set; }
    /// <summary>该次返回的 TOP-N</summary>
    public int TopN { get; set; }
    /// <summary>选股所用策略键</summary>
    public string Strategy { get; set; } = string.Empty;
    /// <summary>策略显示名</summary>
    public string StrategyName { get; set; } = string.Empty;
    /// <summary>LLM 复评状态（pending/running/done/failed/skipped）</summary>
    public string ReviewStatus { get; set; } = "pending";
}

/// <summary>某批选股的"选后表现"汇总</summary>
public class SelectionPerformance
{
    public long Id { get; set; }
    /// <summary>选股所基于的交易日</summary>
    public DateTime SelectionTradingDate { get; set; }
    public DateTime RunAt { get; set; }
    public int Count { get; set; }
    /// <summary>选股所用策略键</summary>
    public string Strategy { get; set; } = string.Empty;
    /// <summary>策略显示名</summary>
    public string StrategyName { get; set; } = string.Empty;
    /// <summary>LLM 复评状态（pending/running/done/failed/skipped）</summary>
    public string ReviewStatus { get; set; } = "pending";
    /// <summary>前进数据截至的最新交易日（无则 null）</summary>
    public DateTime? LatestDate { get; set; }
    /// <summary>至今上涨命中数（自选股日累计涨幅 &gt; 0）</summary>
    public int HitCount { get; set; }
    /// <summary>至今平均累计涨跌幅（%）</summary>
    public decimal AvgCurrentChange { get; set; }
    public List<SelectionPerformanceItem> Items { get; set; } = new();
}

/// <summary>单只选股标的的选后表现</summary>
public class SelectionPerformanceItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>选股日收盘价（成本基准）</summary>
    public decimal SelectClose { get; set; }
    /// <summary>选股当天涨跌幅（%）</summary>
    public decimal SelectChangePercent { get; set; }
    /// <summary>次日(T+1)涨跌幅（%），相对选股日收盘</summary>
    public decimal? NextDayChangePercent { get; set; }
    /// <summary>自选股日至今累计涨跌幅（%）</summary>
    public decimal? CurrentChangePercent { get; set; }
    /// <summary>选中之后最高涨幅（%，区间最高价相对选股日收盘）</summary>
    public decimal? MaxRisePercent { get; set; }
    /// <summary>选中之后最低跌幅（%，区间最低价相对选股日收盘，负值）</summary>
    public decimal? MaxDropPercent { get; set; }
    /// <summary>已观察的前进交易日数</summary>
    public int ForwardDays { get; set; }

    // —— 选中原因（选股当时记录，供历史回看）——
    /// <summary>选股引擎当时生成的核心逻辑（一句话原因）</summary>
    public string CoreLogic { get; set; } = string.Empty;
    /// <summary>选中时的标签（形态/主力/题材等）</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>选中时的评级（1-5 星）</summary>
    public int RatingStars { get; set; }
    /// <summary>选中时的综合评分</summary>
    public decimal TotalScore { get; set; }

    /// <summary>LLM 复评结果（异步补写，可能为 null）</summary>
    public LlmReview? Review { get; set; }
}

/// <summary>
/// 选股结果（一只股票一张卡片）
/// </summary>
public class StockSelectionResult
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>所属行业</summary>
    public string Industry { get; set; } = string.Empty;

    /// <summary>关联概念/题材（命中热门题材的优先排序）</summary>
    public List<string> Concepts { get; set; } = new();

    /// <summary>命中当日热门题材的概念（活跃股集中的风口题材）</summary>
    public List<string> HotConcepts { get; set; } = new();

    /// <summary>命中题材的"炒作点"（概念·LLM蒸馏短语，如"半导体·类ABF膜国产替代"）。供展示，让题材一眼看懂。</summary>
    public List<string> ThemeReasons { get; set; } = new();

    /// <summary>评级（1-5 星）</summary>
    public int RatingStars { get; set; }

    /// <summary>标签（主力+X万 / MACD刚金叉 / 概念…）</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>关联的知识星球"小作文"标题（仅展示提示，不参与排雷/打分；最多取最近几条）。</summary>
    public List<string> KnowledgeStarNotes { get; set; } = new();

    public decimal Close { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal TotalMarketCap { get; set; }
    public decimal Rise20d { get; set; }
    public decimal PeTtm { get; set; }

    /// <summary>BBI 多空指数 =(MA3+MA6+MA12+MA24)/4；收盘价 ≥ BBI 视为多头占优。</summary>
    public decimal Bbi { get; set; }

    /// <summary>主力净流入（元）</summary>
    public decimal MainNetInflow { get; set; }

    /// <summary>连续主力净流入天数（多日序列）</summary>
    public int ConsecutiveInflowDays { get; set; }

    /// <summary>当前连续涨停板数（多日序列）</summary>
    public int ConsecutiveLimitUp { get; set; }

    /// <summary>综合得分</summary>
    public decimal TotalScore { get; set; }

    /// <summary>各因子明细</summary>
    public SelectionFactorScores Factors { get; set; } = new();

    /// <summary>核心逻辑（规则或 LLM 生成）</summary>
    public string CoreLogic { get; set; } = string.Empty;

    /// <summary>选股时的大盘环境描述（同一批选股相同）</summary>
    public string MarketRegime { get; set; } = string.Empty;

    /// <summary>当前市场状态推荐的策略键（lowdip/trend/theme，前端据此提示切换）</summary>
    public string RecommendedStrategy { get; set; } = string.Empty;

    /// <summary>
    /// LLM 复评结果（异步补写，仅对每批前 N 只生成）。null = 未复评 / 不在复评范围内。
    /// 复评不改动选股结果本身（排序/入选），只附加标记。
    /// </summary>
    public LlmReview? Review { get; set; }
}

/// <summary>LLM 复评建议等级</summary>
public enum ReviewRecommendation
{
    /// <summary>回避（明显风险/逻辑证伪）</summary>
    Avoid,
    /// <summary>观望（一般，等更明确信号）</summary>
    Watch,
    /// <summary>建议买入</summary>
    Buy,
}

/// <summary>
/// 选股 LLM 复评结果（附加在 <see cref="StockSelectionResult"/> 上）。
/// 价位由规则计算（<c>Plan</c>），LLM 仅负责标记与解释，不改动数字。
/// </summary>
public class LlmReview
{
    /// <summary>建议等级</summary>
    public ReviewRecommendation Recommendation { get; set; } = ReviewRecommendation.Watch;

    /// <summary>置信度（0-100）</summary>
    public int Confidence { get; set; }

    /// <summary>风险/排雷标签（高位追涨、解禁、问询函、题材证伪…）</summary>
    public List<string> RiskFlags { get; set; } = new();

    /// <summary>情报印证（结合 event_record：消息面是支持还是证伪，一句话）</summary>
    public string IntelligenceNote { get; set; } = string.Empty;

    /// <summary>LLM 核心逻辑叙述（替代/补充规则模板）</summary>
    public string Narrative { get; set; } = string.Empty;

    /// <summary>买入计划（仅"建议买入"时给出；价位规则算，含 LLM 文字解释）</summary>
    public TradePlan? Plan { get; set; }

    /// <summary>使用的模型名</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>复评时间</summary>
    public DateTime ReviewedAt { get; set; }
}

/// <summary>买入计划：价位由规则计算，盈亏比≈2:1。</summary>
public class TradePlan
{
    /// <summary>买入价区间下沿（元）</summary>
    public decimal BuyLow { get; set; }
    /// <summary>买入价区间上沿（元）</summary>
    public decimal BuyHigh { get; set; }
    /// <summary>止损价（元）</summary>
    public decimal StopLoss { get; set; }
    /// <summary>止盈价（元）</summary>
    public decimal TakeProfit { get; set; }
    /// <summary>价位计算依据（基准/支撑/ATR/盈亏比）+ LLM 解释</summary>
    public string Basis { get; set; } = string.Empty;
}
