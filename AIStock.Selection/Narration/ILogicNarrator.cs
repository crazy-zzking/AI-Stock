using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Narration;

/// <summary>
/// 叙述上下文 — 生成核心逻辑文字所需的原始数据。
/// </summary>
public class NarrationContext
{
    public required DailyMarketSnapshotEntity Snapshot { get; init; }
    public DragonTigerEntity? DragonTiger { get; init; }
    public List<string> ActivityFeatures { get; init; } = new();
}

/// <summary>
/// 核心逻辑叙述器。默认规则实现 <see cref="RuleLogicNarrator"/>；可切换 LLM 实现。
/// </summary>
public interface ILogicNarrator
{
    string Narrate(StockSelectionResult result, NarrationContext context);
}
