namespace AIStock.Monitor;

/// <summary>
/// 监控告警配置
/// </summary>
public class MonitorOptions
{
    public const string SectionName = "Monitor";

    /// <summary>是否启用监控</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>检查间隔（秒）</summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>账户总盈亏率告警阈值（百分比，负数）。低于此值告警，如 -5 表示总亏损超 5%</summary>
    public decimal AccountLossPercent { get; set; } = -5m;

    /// <summary>单只持仓亏损率告警阈值（百分比，负数），如 -10 表示个股亏损超 10%</summary>
    public decimal PositionLossPercent { get; set; } = -10m;

    /// <summary>持仓占总资产比例告警阈值（百分比），如 90 表示仓位超 90% 告警</summary>
    public decimal MaxPositionRatioPercent { get; set; } = 90m;

    /// <summary>告警 Webhook 地址（企业微信/钉钉群机器人）；留空则仅写日志</summary>
    public string WebhookUrl { get; set; } = string.Empty;
}
