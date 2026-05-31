namespace AIStock.Core.Models;

/// <summary>
/// 选股条件（两级漏斗参数 + 多因子阈值）
/// </summary>
public class SelectionCriteria
{
    /// <summary>返回 TOP-N</summary>
    public int TopN { get; set; } = 5;

    // —— 第一级：活跃度粗筛 ——
    /// <summary>放量大涨：当日涨幅下限（%）</summary>
    public decimal SurgeChangePercent { get; set; } = 5m;
    /// <summary>放量/震荡：量比下限</summary>
    public decimal MinVolumeRatio { get; set; } = 1.5m;
    /// <summary>放量震荡：振幅下限（%）</summary>
    public decimal ShockAmplitude { get; set; } = 6m;
    /// <summary>近期涨停回看天数（当前快照为单日，置 0 表示仅看当日 IsLimitUp）</summary>
    public int LimitUpLookbackDays { get; set; } = 0;

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
}

/// <summary>
/// 选股结果（一只股票一张卡片）
/// </summary>
public class StockSelectionResult
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

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

    /// <summary>综合得分</summary>
    public decimal TotalScore { get; set; }

    /// <summary>各因子明细</summary>
    public SelectionFactorScores Factors { get; set; } = new();

    /// <summary>核心逻辑（规则或 LLM 生成）</summary>
    public string CoreLogic { get; set; } = string.Empty;
}
