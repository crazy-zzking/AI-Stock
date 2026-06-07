namespace AIStock.Selection.Strategies;

/// <summary>
/// K 线形态策略：以"命中 K 线形态"为硬过滤的内置策略。复用 <see cref="ConfigurableSelectionStrategy"/>
/// 的过滤/打分骨架，口径由 <see cref="BuiltinStrategyDefinitions.KPattern"/> 定义。
/// 形态识别结果由 <see cref="StockSelectionService"/> 预先算好放入 <see cref="SelectionContext.PatternsByCode"/>。
/// </summary>
public sealed class KPatternStrategy : ConfigurableSelectionStrategy
{
    public KPatternStrategy() : base(BuiltinStrategyDefinitions.KPattern()) { }
}
