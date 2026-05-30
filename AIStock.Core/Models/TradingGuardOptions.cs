namespace AIStock.Core.Models;

/// <summary>
/// 交易模式
/// </summary>
public enum TradingMode
{
    /// <summary>
    /// 模拟盘/纸上交易 — 不调用券商接口，仅记录意向单
    /// </summary>
    DryRun = 0,

    /// <summary>
    /// 实盘 — 真实下单
    /// </summary>
    Live = 1
}

/// <summary>
/// 交易安全护栏配置。默认 DryRun，必须显式配置为 Live 才会真实下单。
/// </summary>
public class TradingGuardOptions
{
    public const string SectionName = "Trading";

    /// <summary>
    /// 交易模式，默认 DryRun（防呆：未显式开启实盘前不会花真钱）
    /// </summary>
    public TradingMode Mode { get; set; } = TradingMode.DryRun;

    /// <summary>
    /// 全局熔断开关。为 true 时拒绝所有下单（kill-switch）。
    /// </summary>
    public bool Halted { get; set; }

    /// <summary>
    /// 单笔订单最大金额（元）。0 表示不限制。
    /// </summary>
    public decimal MaxOrderValue { get; set; } = 100000m;

    /// <summary>
    /// 每日最大下单次数。0 表示不限制。
    /// </summary>
    public int MaxOrdersPerDay { get; set; } = 50;
}
