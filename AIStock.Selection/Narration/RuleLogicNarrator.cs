using System.Text;
using AIStock.Core.Models;

namespace AIStock.Selection.Narration;

/// <summary>
/// 规则模板叙述器 — 按命中因子拼接核心逻辑文字，无外部依赖、确定性、可单测。
/// </summary>
public class RuleLogicNarrator : ILogicNarrator
{
    public string Narrate(StockSelectionResult result, NarrationContext context)
    {
        var s = context.Snapshot;
        var parts = new List<string>();

        // 技术面：MACD 金叉初期
        if (s.MacdGoldenCross)
        {
            parts.Add($"MACD 刚金叉(DIF={s.MacdDif:F2}>DEA={s.MacdDea:F2})，处确认初期");
        }

        // 位置：低位 / 非追高
        if (s.Rise20d > 0)
        {
            var pos = s.Rise20d < 20 ? "低位补涨" : s.Rise20d < 35 ? "中位" : "偏高位";
            parts.Add($"20日涨幅 {s.Rise20d:F1}%，属{pos}");
        }

        // 资金面
        if (s.MainNetInflow > 0)
        {
            parts.Add($"主力净流入 {FormatYuan(s.MainNetInflow)}，资金面正向");
        }

        // 龙虎榜
        if (context.DragonTiger != null)
        {
            var dt = context.DragonTiger;
            var inst = dt.HasInstitution ? "含机构席位，" : "";
            parts.Add($"登上龙虎榜（{inst}净买 {FormatYuan(dt.NetBuyAmount)}）");
        }

        // 活跃特征
        if (context.ActivityFeatures.Count > 0)
        {
            parts.Add($"活跃特征：{string.Join("、", context.ActivityFeatures)}");
        }

        // RSI 状态
        if (s.Rsi > 0)
        {
            var rsiState = s.Rsi >= 70 ? "偏超买需警惕" : s.Rsi >= 50 ? "多头未超买" : "蓄势";
            parts.Add($"RSI {s.Rsi:F0}（{rsiState}）");
        }

        if (parts.Count == 0)
        {
            return "综合多因子打分入选，详见各项得分。";
        }

        var sb = new StringBuilder();
        sb.Append(string.Join("；", parts));
        sb.Append('。');
        return sb.ToString();
    }

    /// <summary>金额格式化为"亿/万"。</summary>
    private static string FormatYuan(decimal yuan)
    {
        var abs = Math.Abs(yuan);
        if (abs >= 100_000_000m) return $"{yuan / 100_000_000m:F2}亿";
        if (abs >= 10_000m) return $"{yuan / 10_000m:F0}万";
        return $"{yuan:F0}元";
    }
}
