namespace AIStock.Core.Models;

/// <summary>
/// DeepSeek 账户余额（实时查询，不入库）
/// </summary>
public class DeepSeekBalance
{
    /// <summary>
    /// 是否成功查询
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 账户是否有可用余额
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// 各币种余额明细
    /// </summary>
    public List<DeepSeekBalanceInfo> BalanceInfos { get; set; } = new();

    /// <summary>
    /// 错误信息（查询失败时）
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 单币种余额明细
/// </summary>
public class DeepSeekBalanceInfo
{
    /// <summary>
    /// 币种（CNY / USD）
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// 总可用余额（赠金 + 充值）
    /// </summary>
    public string TotalBalance { get; set; } = string.Empty;

    /// <summary>
    /// 未过期赠金余额
    /// </summary>
    public string GrantedBalance { get; set; } = string.Empty;

    /// <summary>
    /// 充值余额
    /// </summary>
    public string ToppedUpBalance { get; set; } = string.Empty;
}
