namespace AIStock.Selection.Strategies;

/// <summary>
/// 可配置策略的"口径"定义（DefinitionJson 反序列化目标）。
/// 描述一个数据驱动策略如何过滤、用哪种因子口径、是否惩罚——权重/阈值另由 SelectionCriteria 提供。
/// 现有内置策略(lowdip/trend/theme)都能用本结构表达，证明其表达力。
/// </summary>
public class StrategyDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PreferredRegime { get; set; } = string.Empty;

    /// <summary>硬过滤开关集合</summary>
    public StrategyFilters Filters { get; set; } = new();

    /// <summary>因子口径选择（低吸/趋势）</summary>
    public StrategyFactorKinds FactorKinds { get; set; } = new();

    /// <summary>惩罚口径：none（不罚）| limitup（涨停/连板追高惩罚）</summary>
    public string Penalty { get; set; } = "limitup";

    /// <summary>核心逻辑文案模板（留空则用通用模板）</summary>
    public string? CoreLogicTemplate { get; set; }

    /// <summary>是否扫描全市场（跳过活跃度粗筛）。形态类策略=true。</summary>
    public bool ScanFullUniverse { get; set; }
}

/// <summary>硬过滤开关。复用 SelectionCriteria 的阈值（MaxRsi/MaxRise20d/MinMainNetInflow…），这里只控制"启不启用某条过滤"。</summary>
public class StrategyFilters
{
    /// <summary>按 MaxRsi 过滤超买（低吸/题材=true；趋势=false 容忍强势）</summary>
    public bool ByRsi { get; set; } = true;
    /// <summary>按 MaxRise20d 过滤追高</summary>
    public bool ByRise20d { get; set; } = true;
    /// <summary>按 MinMainNetInflow 过滤资金</summary>
    public bool ByMinInflow { get; set; } = true;
    /// <summary>要求站上 MA20（趋势核心）</summary>
    public bool RequireAboveMa20 { get; set; }
    /// <summary>要求命中当日热门题材（题材策略核心门槛）</summary>
    public bool RequireHotConcept { get; set; }
    /// <summary>排除"传统低弹性行业 + 大市值"</summary>
    public bool ExcludeTraditionalBigCap { get; set; } = true;
    /// <summary>极端追高硬顶（%）；为 null 不限。趋势用较大值(如100)只挡极端透支</summary>
    public decimal? ExtremeRise20d { get; set; }
    /// <summary>弱市把 MaxRise20d 收紧到 30（低吸/题材）</summary>
    public bool WeakRegimeTightenRise20d { get; set; } = true;
    /// <summary>弱市要求主力净流入 &gt;0（低吸）</summary>
    public bool WeakRegimeRequireInflow { get; set; } = true;

    /// <summary>
    /// 要求命中的 K 线形态键（取值见 <see cref="AIStock.Selection.CandlePatternAnalyzer.PatternKeys"/>）。
    /// 非空时启用形态硬过滤：默认 OR（命中任一即过），<see cref="RequireAllPatterns"/>=true 则要求全部命中。空=不启用。
    /// </summary>
    public List<string> RequirePatterns { get; set; } = new();

    /// <summary>形态过滤是否要求全部命中（默认 false=命中任一即可）。</summary>
    public bool RequireAllPatterns { get; set; }
}

/// <summary>因子口径选择：lowdip（低吸口径）| trend（趋势口径）。其余因子两口径一致。</summary>
public class StrategyFactorKinds
{
    /// <summary>技术面：lowdip（RSI 超买不加分）| trend（容忍高 RSI、均线多头权重更大）</summary>
    public string Technical { get; set; } = "lowdip";
    /// <summary>位置：lowdip（越低越好）| trend（中段甜区）</summary>
    public string Position { get; set; } = "lowdip";
}
