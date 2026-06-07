using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Entities;

namespace AIStock.Selection;

/// <summary>
/// 第一级漏斗：活跃度粗筛。"要大涨必须近期有量"——先筛出活跃股，淘汰不活跃标的。
/// 命中任一活跃特征即入池，命中越多活跃分越高。纯计算，无外部依赖，便于单测。
/// </summary>
public static class ActivityScreener
{
    /// <summary>一只股票的活跃度评估结果</summary>
    public record ActivityHit(DailyMarketSnapshotEntity Snapshot, decimal ActivityScore, List<string> Features);

    /// <summary>
    /// 对快照列表做活跃度粗筛，返回活跃池（按活跃分降序）。
    /// </summary>
    public static List<ActivityHit> Screen(IEnumerable<DailyMarketSnapshotEntity> snapshots, SelectionCriteria criteria)
    {
        var pool = new List<ActivityHit>();
        foreach (var s in snapshots)
        {
            var (score, features) = Evaluate(s, criteria);
            if (features.Count > 0) pool.Add(new ActivityHit(s, score, features));
        }
        return pool.OrderByDescending(h => h.ActivityScore).ToList();
    }

    /// <summary>
    /// 全市场池：保留全部股票（含活跃分为 0 的不活跃股），仅计算活跃分供后续因子打分/排序。
    /// 供需要扫全市场的策略（如形态类）使用。
    /// </summary>
    public static List<ActivityHit> ScreenAll(IEnumerable<DailyMarketSnapshotEntity> snapshots, SelectionCriteria criteria)
    {
        var pool = new List<ActivityHit>();
        foreach (var s in snapshots)
        {
            var (score, features) = Evaluate(s, criteria);
            pool.Add(new ActivityHit(s, score, features));
        }
        return pool.OrderByDescending(h => h.ActivityScore).ToList();
    }

    /// <summary>单只股票活跃度评估：命中任一活跃特征加分，返回(活跃分, 命中特征)。</summary>
    private static (decimal Score, List<string> Features) Evaluate(DailyMarketSnapshotEntity s, SelectionCriteria criteria)
    {
        var features = new List<string>();
        decimal score = 0;

        // 温和放量上涨（埋伏首选）：已启动但未涨停、涨幅适中 + 放量 → 次日高开概率低、可低吸
        if (!s.IsLimitUp
            && s.ChangePercent >= criteria.HealthyRiseMin
            && s.ChangePercent <= criteria.HealthyRiseMax
            && s.VolumeRatio >= criteria.MinVolumeRatio)
        {
            features.Add("温和放量");
            score += 35;
        }

        // 资金流入
        if (s.MainNetInflow > 0)
        {
            features.Add("资金流入");
            score += 25;
        }

        // 放量震荡
        if (s.Amplitude >= criteria.ShockAmplitude && s.VolumeRatio >= criteria.MinVolumeRatio)
        {
            features.Add("放量震荡");
            score += 15;
        }

        // 涨停：仅作"有资金关注"的弱信号，低分（次日高开，非埋伏首选）
        if (s.IsLimitUp)
        {
            features.Add("涨停");
            score += 10;
        }

        return (score, features);
    }
}
