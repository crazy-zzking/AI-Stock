using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;
using AIStock.Selection;

namespace AIStock.Tests;

/// <summary>
/// 吸筹质量分（不含动量）口径测试：缩量/波动收敛/贴 MA10/资金承接 越好分越高。
/// </summary>
public class AmbushQualityTests
{
    [Fact]
    public void TightContractedNearMa_ScoresHigherThan_LooseFarBleeding()
    {
        // 优质吸筹：缩量(0.6)、贴 MA10(10.1/10=1.01)、资金净流入
        var good = new DailyMarketSnapshotEntity { Close = 10.1m, Ma10 = 10.0m, VolumeRatio = 0.6m };
        var goodSeq = new SequenceFeatures { AvgAmplitude5 = 3m, AmplitudeConverging = true, CumNetInflow5 = 5_000_000m, ConsecutiveInflowDays = 3 };

        // 劣质：放量(1.5)、离均线远(11/10=1.1)、资金流出、振幅大
        var bad = new DailyMarketSnapshotEntity { Close = 11.0m, Ma10 = 10.0m, VolumeRatio = 1.5m };
        var badSeq = new SequenceFeatures { AvgAmplitude5 = 11m, AmplitudeConverging = false, CumNetInflow5 = -5_000_000m };

        var qGood = SelectionScorers.AmbushQuality(good, goodSeq);
        var qBad = SelectionScorers.AmbushQuality(bad, badSeq);

        Assert.True(qGood > qBad, $"优质吸筹分应高于劣质：good={qGood} bad={qBad}");
        Assert.True(qGood >= 80m, $"优质吸筹分应较高：{qGood}");
    }

    [Fact]
    public void Score_IsMomentumFree_IndependentOfRiseAndChange()
    {
        // 同样的吸筹形态，涨幅/位置不同不应改变质量分（验证不含动量）
        var seq = new SequenceFeatures { AvgAmplitude5 = 3m, AmplitudeConverging = true, CumNetInflow5 = 1m };
        var a = new DailyMarketSnapshotEntity { Close = 10.1m, Ma10 = 10m, VolumeRatio = 0.6m, Rise20d = 5m, ChangePercent = 1m };
        var b = new DailyMarketSnapshotEntity { Close = 10.1m, Ma10 = 10m, VolumeRatio = 0.6m, Rise20d = 80m, ChangePercent = 9m };

        Assert.Equal(SelectionScorers.AmbushQuality(a, seq), SelectionScorers.AmbushQuality(b, seq));
    }
}
