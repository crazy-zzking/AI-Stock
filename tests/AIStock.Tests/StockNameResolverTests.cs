using AIStock.EventEngine.Services;

namespace AIStock.Tests;

/// <summary>
/// 事件关联个股"名称→代码"归一测试（纯计算）。覆盖代码直用/全名精确/模糊唯一/模糊多义跳过/短片段跳过。
/// </summary>
public class StockNameResolverTests
{
    private static readonly (string Name, string Code)[] Universe =
    {
        ("宁德时代", "300750"),
        ("比亚迪", "002594"),
        ("中国平安", "601318"),
        ("中国银行", "601988"),
        ("中国石化", "600028"),
        ("贵州茅台", "600519"),
    };

    [Fact]
    public void SixDigitCode_UsedDirectly()
    {
        var r = StockNameResolver.ResolveCodes(new[] { "300750" }, Universe);
        Assert.Contains("300750", r.Codes);
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public void ExactName_Resolved()
    {
        var r = StockNameResolver.ResolveCodes(new[] { "贵州茅台" }, Universe);
        Assert.Contains("600519", r.Codes);
    }

    [Fact]
    public void PartialName_UniqueFuzzy_Resolved()
    {
        // "宁德" 残缺名，全市场只有"宁德时代"包含它 → 归一
        var r = StockNameResolver.ResolveCodes(new[] { "宁德" }, Universe);
        Assert.Contains("300750", r.Codes);
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public void PartialName_Ambiguous_SkippedWhenOverLimit()
    {
        // "中国" 命中 平安/银行/石化 3 只；上限设 2 → 过泛跳过
        var r = StockNameResolver.ResolveCodes(new[] { "中国" }, Universe, fuzzyMaxAmbiguity: 2);
        Assert.Empty(r.Codes);
        Assert.Contains("中国", r.Unresolved);
    }

    [Fact]
    public void PartialName_WithinLimit_ResolvesAll()
    {
        // "中国" 命中 3 只，上限 3 → 全部归一（按用户选择的模糊包含口径）
        var r = StockNameResolver.ResolveCodes(new[] { "中国" }, Universe, fuzzyMaxAmbiguity: 3);
        Assert.Equal(3, r.Codes.Count);
        Assert.Contains("601318", r.Codes);
        Assert.Contains("601988", r.Codes);
        Assert.Contains("600028", r.Codes);
    }

    [Fact]
    public void SingleChar_TooShort_Skipped()
    {
        // 单字"中"不做模糊，避免无意义爆链
        var r = StockNameResolver.ResolveCodes(new[] { "中" }, Universe);
        Assert.Empty(r.Codes);
        Assert.Contains("中", r.Unresolved);
    }

    [Fact]
    public void Unknown_Skipped()
    {
        var r = StockNameResolver.ResolveCodes(new[] { "不存在的公司XYZ" }, Universe);
        Assert.Empty(r.Codes);
        Assert.Single(r.Unresolved);
    }

    [Fact]
    public void Mixed_DedupAndResolve()
    {
        var r = StockNameResolver.ResolveCodes(
            new[] { "300750", "宁德", "比亚迪", "比亚迪", "  " }, Universe);
        // 300750 与 "宁德" 都归到 300750，去重；比亚迪→002594
        Assert.Contains("300750", r.Codes);
        Assert.Contains("002594", r.Codes);
        Assert.Equal(2, r.Codes.Count);
    }
}
