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

    /// <summary>是否启用 LLM 生成核心逻辑（默认规则模板）</summary>
    public bool UseLlmNarrative { get; set; } = false;
}

/// <summary>大盘环境等级</summary>
public enum MarketRegimeLevel { Weak, Neutral, Strong }

/// <summary>单个指数行情快照</summary>
public class IndexQuote
{
    public string Name { get; set; } = string.Empty;
    /// <summary>当日涨跌幅（%）</summary>
    public decimal ChangePercent { get; set; }
    /// <summary>是否站上 20 日均线</summary>
    public bool AboveMa20 { get; set; }
}

/// <summary>
/// 大盘环境判断 — 综合多个主要指数（上证/深成/创业板/沪深300 的涨跌幅与是否站上20日线）
/// 与全市场涨跌广度，用于调节选股松紧。
/// </summary>
public class MarketRegime
{
    public MarketRegimeLevel Level { get; set; } = MarketRegimeLevel.Neutral;
    /// <summary>参与判断的各指数快照</summary>
    public List<IndexQuote> Indices { get; set; } = new();
    /// <summary>是否取到至少一个指数（取不到则仅用广度判断）</summary>
    public bool HasIndex => Indices.Count > 0;
    /// <summary>全市场上涨家数占比（0-1）</summary>
    public decimal AdvanceRatio { get; set; }
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

    /// <summary>评级（1-5 星）</summary>
    public int RatingStars { get; set; }

    /// <summary>标签（主力+X万 / MACD刚金叉 / 概念…）</summary>
    public List<string> Tags { get; set; } = new();

    public decimal Close { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal TotalMarketCap { get; set; }
    public decimal Rise20d { get; set; }
    public decimal PeTtm { get; set; }

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
}
