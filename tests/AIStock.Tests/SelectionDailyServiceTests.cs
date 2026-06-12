using AIStock.Core.Models;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>每日选股报告格式化（纯计算）。</summary>
public class SelectionDailyReportTests
{
    private static StockSelectionResult Pick(string code, string name, bool whisper = false) => new()
    {
        Code = code,
        Name = name,
        KnowledgeStarNotes = whisper ? new List<string> { "小作文" } : new(),
    };

    [Fact]
    public void FormatReport_ListsTop3_MarksWhisper_SkipsEmpty()
    {
        var run = new DailySelectionRun { Ok = 3, Signals = 5 };
        run.PicksByStrategy.Add(("题材动量", new List<StockSelectionResult>
        {
            Pick("600001", "甲", whisper: true), Pick("600002", "乙"), Pick("600003", "丙"), Pick("600004", "丁"),
        }));
        run.PicksByStrategy.Add(("吸筹埋伏", new List<StockSelectionResult> { Pick("000001", "戊") }));
        run.PicksByStrategy.Add(("K线形态", new List<StockSelectionResult>()));

        var text = run.FormatReport("📊 测试报告");

        Assert.Contains("📊 测试报告", text);
        Assert.Contains("题材动量: 甲(600001)✉、乙(600002)、丙(600003) 等4只", text);
        Assert.Contains("吸筹埋伏: 戊(000001)", text);
        Assert.DoesNotContain("K线形态", text);    // 空策略不列
        Assert.Contains("共 5 个信号", text);
        Assert.DoesNotContain("⚠", text);          // 无失败不显示警告
    }

    [Fact]
    public void FormatReport_ShowsFailureWarning()
    {
        var run = new DailySelectionRun { Ok = 6, Failed = 1, Signals = 10 };
        var text = run.FormatReport("t");
        Assert.Contains("⚠ 1 个策略执行失败", text);
    }
}
