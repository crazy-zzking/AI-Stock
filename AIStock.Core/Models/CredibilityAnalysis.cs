namespace AIStock.Core.Models;

/// <summary>
/// 真假识别结果
/// </summary>
public class CredibilityResult
{
    /// <summary>
    /// 可信度评分（0-100）
    /// </summary>
    public int CredibilityScore { get; set; }

    /// <summary>
    /// 是否有历史重复
    /// </summary>
    public bool HasHistoricalDuplicate { get; set; }

    /// <summary>
    /// 历史重复事件列表
    /// </summary>
    public List<string> DuplicateEvents { get; set; } = new();

    /// <summary>
    /// 逻辑闭环评分（0-100）
    /// </summary>
    public int LogicScore { get; set; }

    /// <summary>
    /// 逻辑分析
    /// </summary>
    public string? LogicAnalysis { get; set; }

    /// <summary>
    /// 资金配合评分（0-100）
    /// </summary>
    public int CapitalScore { get; set; }

    /// <summary>
    /// 资金分析
    /// </summary>
    public string? CapitalAnalysis { get; set; }

    /// <summary>
    /// 综合判断（real/fake/uncertain）
    /// </summary>
    public string Verdict { get; set; } = "uncertain";

    /// <summary>
    /// 判断理由
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 风险提示
    /// </summary>
    public List<string> RiskWarnings { get; set; } = new();
}

/// <summary>
/// 传播链分析结果
/// </summary>
public class SpreadAnalysisResult
{
    /// <summary>
    /// 首发源
    /// </summary>
    public string? FirstSource { get; set; }

    /// <summary>
    /// 首发时间
    /// </summary>
    public DateTime? FirstTime { get; set; }

    /// <summary>
    /// 传播路径
    /// </summary>
    public List<SpreadNode> SpreadPath { get; set; } = new();

    /// <summary>
    /// 当前热度（0-100）
    /// </summary>
    public int CurrentHeat { get; set; }

    /// <summary>
    /// 热度斜率（每小时变化）
    /// </summary>
    public double HeatSlope { get; set; }

    /// <summary>
    /// 传播速度评级（slow/medium/fast/viral）
    /// </summary>
    public string SpreadSpeed { get; set; } = "medium";

    /// <summary>
    /// 预计热度峰值时间
    /// </summary>
    public DateTime? PeakTime { get; set; }

    /// <summary>
    /// 分析结论
    /// </summary>
    public string Conclusion { get; set; } = string.Empty;
}

/// <summary>
/// 传播节点
/// </summary>
public class SpreadNode
{
    /// <summary>
    /// 平台/来源
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 时间
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// 内容摘要
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 影响力（0-100）
    /// </summary>
    public int Influence { get; set; }
}
