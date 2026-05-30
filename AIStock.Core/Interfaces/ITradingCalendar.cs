namespace AIStock.Core.Interfaces;

/// <summary>
/// 交易日历 — 从外部接口拉取 A 股交易日，供各模块复用
/// </summary>
public interface ITradingCalendar
{
    /// <summary>
    /// 判断指定日期是否为交易日
    /// </summary>
    Task<bool> IsTradingDayAsync(DateTime date, CancellationToken ct = default);

    /// <summary>
    /// 获取区间内的交易日集合
    /// </summary>
    Task<HashSet<DateTime>> FetchTradingDaysAsync(DateTime start, DateTime end, CancellationToken ct = default);
}
