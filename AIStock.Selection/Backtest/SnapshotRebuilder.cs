using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection.Backtest;

/// <summary>
/// 从日 K（+历史资金流）重建某交易日的市场快照（内存），供回放回测脱离 daily_market_snapshot。
/// 技术面(MA/MACD/RSI)复用 IFeatureCalculator，与实盘同口径；量比/均价为 K 线近似
/// （实盘取自行情接口）；市值/PE 历史不可得置 0（仅"传统大盘股排除"过滤受影响）。
/// </summary>
public static class SnapshotRebuilder
{
    /// <summary>
    /// 用"截至当日（升序）"的日 K 重建快照。klines 末根即当日；不足 21 根返回 null。
    /// </summary>
    public static DailyMarketSnapshotEntity? Rebuild(
        string code, string name, IReadOnlyList<KlineData> klinesUpToDay, decimal mainNetInflow, IFeatureCalculator fc)
    {
        if (klinesUpToDay.Count < 21) return null;

        var ordered = klinesUpToDay.OrderBy(k => k.DateTime).ToList();
        var last = ordered[^1];
        var prevClose = ordered.Count >= 2 ? ordered[^2].Close : last.Open;
        var close20Ago = ordered[^21].Close;

        var ma = fc.CalculateMA(ordered);
        var macd = fc.CalculateMACD(ordered);
        var rsi = fc.CalculateRSI(ordered);
        var bbi = fc.CalculateBBI(ordered);

        var changePercent = prevClose > 0 ? (last.Close - prevClose) / prevClose * 100m : 0m;
        var rise20d = close20Ago > 0 ? (last.Close - close20Ago) / close20Ago * 100m : 0m;
        var amplitude = prevClose > 0 ? (last.High - last.Low) / prevClose * 100m : 0m;

        // 量比 ≈ 当日量 / 过去5日均量（不含当日）；均价 ≈ VWAP = 成交额/成交量
        var prev5 = ordered.TakeLast(6).SkipLast(1).Select(k => (decimal)k.Volume).Where(v => v > 0).ToList();
        var vol5 = prev5.Count > 0 ? prev5.Average() : 0m;
        var volumeRatio = vol5 > 0 ? Math.Round(last.Volume / vol5, 2) : 0m;
        var avgPrice = last.Volume > 0 ? Math.Round(last.Amount / last.Volume, 3) : last.Close;

        var threshold = LimitUpThreshold(code);
        var isLimitUp = changePercent >= threshold - 0.3m;
        var goldenCross = macd.DIF > macd.DEA && macd.DIF > -0.05m;

        return new DailyMarketSnapshotEntity
        {
            Code = code,
            Name = name,
            Date = last.DateTime.Date,
            Close = last.Close,
            High = last.High,
            Low = last.Low,
            ChangePercent = changePercent,
            TurnoverRate = last.TurnoverRate,
            VolumeRatio = volumeRatio,
            Amplitude = amplitude,
            AvgPrice = avgPrice,
            IsLimitUp = isLimitUp,
            TotalMarketCap = 0m, // 历史市值不可得（回放不做大盘股市值排除）
            PeTtm = 0m,
            MainNetInflow = mainNetInflow,
            Rise20d = rise20d,
            Ma5 = ma.MA5 ?? 0,
            Ma10 = ma.MA10 ?? 0,
            Ma20 = ma.MA20 ?? 0,
            MacdDif = macd.DIF,
            MacdDea = macd.DEA,
            MacdGoldenCross = goldenCross,
            Rsi = rsi.RSI12,
            Bbi = bbi,
        };
    }

    /// <summary>涨停阈值（%）按板型：创业板/科创板 20，北交所 30，主板 10。</summary>
    private static decimal LimitUpThreshold(string code)
    {
        if (code.StartsWith("30") || code.StartsWith("688")) return 20m; // 创业板 / 科创板
        if (code.StartsWith("8") || code.StartsWith("4") || code.StartsWith("920")) return 30m; // 北交所
        return 10m; // 主板
    }
}
