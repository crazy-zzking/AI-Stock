using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;
using AIStock.Selection.Narration;
using AIStock.Selection.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AIStock.Tests;

/// <summary>
/// 选股端到端测试（EF InMemory）：覆盖 StockSelectionService 的 DB 编排
/// （取最新交易日快照 + join 龙虎榜 → 两级漏斗 → TOP-N）。
/// 快照数值参考真实东财量级（英杰电气 300820：市值≈144亿、PE≈99）。
/// </summary>
public class StockSelectionServiceTests
{
    private readonly ITestOutputHelper _output;
    public StockSelectionServiceTests(ITestOutputHelper output) => _output = output;

    private static AIStockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AIStockDbContext>()
            .UseInMemoryDatabase($"sel-svc-{Guid.NewGuid()}")
            .Options);

    private static StockSelectionService NewService(AIStockDbContext db)
    {
        var engine = new StockSelectionEngine(new RuleLogicNarrator());
        var strategies = new ISelectionStrategy[]
        {
            new LowDipStrategy(engine), new TrendStrategy(), new ThemeStrategy(),
        };
        var provider = new SelectionStrategyProvider(strategies, db, NullLogger<SelectionStrategyProvider>.Instance);
        return new(db, provider, new EmptyResolver(),
            new SelectionConfigService(db, NullLogger<SelectionConfigService>.Instance),
            NullLogger<StockSelectionService>.Instance);
    }

    /// <summary>空数据源解析器：测试中不取指数，大盘环境仅由快照广度判断。</summary>
    private sealed class EmptyResolver : IDataProviderResolver
    {
        public IEnumerable<IDataProvider> GetProviders(DataCapability capability) => Array.Empty<IDataProvider>();
        public IDataProvider? GetPrimaryProvider(DataCapability capability) => null;
        public void RegisterProvider(IDataProvider provider) { }
        public IEnumerable<IDataProvider> GetAllProviders() => Array.Empty<IDataProvider>();
        public IDataProvider GetDefaultProvider() => throw new NotSupportedException();
    }

    private static DailyMarketSnapshotEntity Snap(
        string code, string name, decimal close, decimal change, decimal volRatio,
        decimal mainNet, decimal marketCap, decimal pe, decimal rise20d,
        bool macdGolden = true, decimal rsi = 60, bool limitUp = false, decimal amplitude = 0,
        decimal ma20 = 0) =>
        new()
        {
            Code = code,
            Name = name,
            Date = new DateTime(2026, 5, 12),
            Close = close,
            ChangePercent = change,
            VolumeRatio = volRatio,
            MainNetInflow = mainNet,
            TotalMarketCap = marketCap,
            PeTtm = pe,
            Rise20d = rise20d,
            MacdGoldenCross = macdGolden,
            Rsi = rsi,
            IsLimitUp = limitUp,
            Amplitude = amplitude,
            Ma20 = ma20 == 0 ? close * 0.95m : ma20 // 默认价在 MA20 之上
        };

    [Fact]
    public async Task SelectAsync_EndToEnd_ProducesValidTopN()
    {
        using var db = NewDb();

        // 优质活跃：放量大涨 + 主力净流入 7913万 + MACD金叉 + 低位（真实市值/PE 量级）
        db.DailyMarketSnapshot.Add(Snap("300820", "英杰电气", 64.89m, 6.5m, 2.5m,
            mainNet: 79_130_000m, marketCap: 14_421_997_689m, pe: 99.23m, rise20d: 20m));
        // 活跃：涨停 + 资金流入（带龙虎榜）
        db.DailyMarketSnapshot.Add(Snap("000777", "中核科技", 18.5m, 10m, 3m,
            mainNet: 50_000_000m, marketCap: 8_000_000_000m, pe: 45m, rise20d: 25m, limitUp: true));
        // 不活跃：缩量、微涨、资金流出 → 第一级粗筛淘汰
        db.DailyMarketSnapshot.Add(Snap("600519", "贵州茅台", 1500m, 0.3m, 0.6m,
            mainNet: -10_000_000m, marketCap: 1_800_000_000_000m, pe: 30m, rise20d: 2m));
        // 追高：放量大涨但 20 日涨幅 80% > 上限 50% → 第二级硬过滤淘汰
        db.DailyMarketSnapshot.Add(Snap("301536", "星宸科技", 90m, 8m, 2.2m,
            mainNet: 30_000_000m, marketCap: 6_000_000_000m, pe: 120m, rise20d: 80m));

        // 龙虎榜：000777 当日上榜含机构
        db.DragonTiger.Add(new DragonTigerEntity
        {
            Code = "000777", Name = "中核科技", Date = new DateTime(2026, 5, 12),
            Reason = "日涨幅偏离值达7%", NetBuyAmount = 35_000_000m, HasInstitution = true
        });
        await db.SaveChangesAsync();

        var results = await NewService(db).SelectAsync(new SelectionCriteria { TopN = 5 });

        // 4 只里：贵州茅台（不活跃）+ 星宸科技（追高）被淘汰，余 2 只入选
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.Code == "600519"); // 不活跃淘汰
        Assert.DoesNotContain(results, r => r.Code == "301536"); // 追高淘汰
        // 埋伏型：温和放量未涨停的英杰电气应优于当日涨停的中核科技（规避次日高开）
        Assert.Equal("300820", results[0].Code);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.CoreLogic)));
        Assert.All(results, r => Assert.True(r.RatingStars is >= 1 and <= 5));

        // 市值/PE 透传正确（真实量级）
        var yj = results.Single(r => r.Code == "300820");
        Assert.Equal(14_421_997_689m, yj.TotalMarketCap);
        Assert.Equal(99.23m, yj.PeTtm);

        // 输出 TOP-N 面板（像样图那样）
        _output.WriteLine("===== 明日可介入 — 短线弹性品种 TOP" + results.Count + " =====");
        var rank = 1;
        foreach (var r in results)
        {
            _output.WriteLine($"No.{rank++}  {r.Name}({r.Code})  {new string('★', r.RatingStars)}  评分 {r.TotalScore}");
            _output.WriteLine($"   标签：{string.Join(" / ", r.Tags)}");
            _output.WriteLine($"   收盘 {r.Close}  今日 {r.ChangePercent:+0.##;-0.##}%  市值 {r.TotalMarketCap / 1e8m:F1}亿  20日 {r.Rise20d:+0.##}%  PE {r.PeTtm}");
            _output.WriteLine($"   核心逻辑：{r.CoreLogic}");
            _output.WriteLine($"   因子(资金/技术/位置/龙虎/活跃)：{r.Factors.Capital}/{r.Factors.Technical}/{r.Factors.Position}/{r.Factors.DragonTiger}/{r.Factors.Activity}");
        }
    }
}
