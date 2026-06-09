using AIStock.Core.Models;
using AIStock.Feature.Services;

namespace AIStock.Tests;

/// <summary>
/// BBI 多空指数计算测试（纯函数）。BBI =(MA3+MA6+MA12+MA24)/4，不足 24 根返回 0。
/// </summary>
public class FeatureCalculatorBbiTests
{
    private static readonly FeatureCalculatorService Calc = new();

    private static List<KlineData> Klines(params decimal[] closes)
    {
        var list = new List<KlineData>();
        var d = new DateTime(2026, 1, 1);
        for (int i = 0; i < closes.Length; i++)
            list.Add(new KlineData { DateTime = d.AddDays(i), Close = closes[i] });
        return list;
    }

    [Fact]
    public void Bbi_NotEnoughBars_ReturnsZero()
    {
        var klines = Klines(Enumerable.Repeat(10m, 23).ToArray());
        Assert.Equal(0m, Calc.CalculateBBI(klines));
    }

    [Fact]
    public void Bbi_ConstantPrice_EqualsPrice()
    {
        var klines = Klines(Enumerable.Repeat(10m, 30).ToArray());
        Assert.Equal(10m, Calc.CalculateBBI(klines));
    }

    [Fact]
    public void Bbi_AveragesFourMovingAverages()
    {
        // 前 23 根 10，末根 22 → MA3=14, MA6=12, MA12=11, MA24=10.5 → BBI=11.875
        var closes = Enumerable.Repeat(10m, 23).Append(22m).ToArray();
        Assert.Equal(11.875m, Calc.CalculateBBI(Klines(closes)));
    }
}
