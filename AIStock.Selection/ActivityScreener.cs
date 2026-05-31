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
            var features = new List<string>();
            decimal score = 0;

            // 放量大涨
            if (s.ChangePercent >= criteria.SurgeChangePercent && s.VolumeRatio >= criteria.MinVolumeRatio)
            {
                features.Add("放量大涨");
                score += 30;
            }

            // 资金流入
            if (s.MainNetInflow > 0)
            {
                features.Add("资金流入");
                score += 25;
            }

            // 近期涨停
            if (s.IsLimitUp)
            {
                features.Add("涨停");
                score += 30;
            }

            // 放量震荡
            if (s.Amplitude >= criteria.ShockAmplitude && s.VolumeRatio >= criteria.MinVolumeRatio)
            {
                features.Add("放量震荡");
                score += 15;
            }

            if (features.Count > 0)
            {
                pool.Add(new ActivityHit(s, score, features));
            }
        }

        return pool.OrderByDescending(h => h.ActivityScore).ToList();
    }
}
