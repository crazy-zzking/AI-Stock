namespace AIStock.Selection.Strategies;

/// <summary>
/// 内置三策略(lowdip/trend/theme)的 <see cref="StrategyDefinition"/> 表达。
/// 用途：① 证明可配置框架的表达力（能复刻现有策略）；② 前端"以内置为模板克隆新策略"。
/// 注意：实盘仍由代码内置策略类执行，这里仅作模板/验收参照。
/// </summary>
public static class BuiltinStrategyDefinitions
{
    public static StrategyDefinition LowDip() => new()
    {
        Key = StrategyKeys.LowDip,
        Name = "低吸埋伏",
        Description = "规避追高，偏好低位、温和放量、回踩企稳的中小盘成长股（左侧/埋伏）",
        PreferredRegime = "弱市 / 震荡市",
        Filters = new StrategyFilters
        {
            ByRsi = true, ByRise20d = true, ByMinInflow = true,
            RequireAboveMa20 = false, RequireHotConcept = false,
            ExcludeTraditionalBigCap = true, ExtremeRise20d = null,
            WeakRegimeTightenRise20d = true, WeakRegimeRequireInflow = true,
        },
        FactorKinds = new StrategyFactorKinds { Technical = "lowdip", Position = "lowdip" },
        Penalty = "limitup",
    };

    public static StrategyDefinition Trend() => new()
    {
        Key = StrategyKeys.Trend,
        Name = "趋势跟随",
        Description = "追逐均线多头发散、量价齐升的强势股，容忍高 RSI（右侧追强）",
        PreferredRegime = "强市 / 主升",
        Filters = new StrategyFilters
        {
            ByRsi = false, ByRise20d = false, ByMinInflow = true,
            RequireAboveMa20 = true, RequireHotConcept = false,
            ExcludeTraditionalBigCap = true, ExtremeRise20d = 100m,
            WeakRegimeTightenRise20d = false, WeakRegimeRequireInflow = false,
        },
        FactorKinds = new StrategyFactorKinds { Technical = "trend", Position = "trend" },
        Penalty = "none",
    };

    public static StrategyDefinition Theme() => new()
    {
        Key = StrategyKeys.Theme,
        Name = "题材龙头",
        Description = "只选命中当日热门题材的个股，侧重题材合力/板块强度/龙头带动",
        PreferredRegime = "题材市 / 情绪活跃",
        Filters = new StrategyFilters
        {
            ByRsi = true, ByRise20d = true, ByMinInflow = true,
            RequireAboveMa20 = false, RequireHotConcept = true,
            ExcludeTraditionalBigCap = true, ExtremeRise20d = null,
            WeakRegimeTightenRise20d = true, WeakRegimeRequireInflow = false,
        },
        FactorKinds = new StrategyFactorKinds { Technical = "lowdip", Position = "lowdip" },
        Penalty = "limitup",
    };
}
