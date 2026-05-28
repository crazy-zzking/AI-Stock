namespace AIStock.Core.Enums;

/// <summary>
/// 仓位管理模式
/// </summary>
public enum PositionSizeMode
{
    /// <summary>
    /// Kelly公式
    /// </summary>
    Kelly,

    /// <summary>
    /// 风险平价
    /// </summary>
    RiskParity,

    /// <summary>
    /// 波动率目标
    /// </summary>
    VolatilityTarget,

    /// <summary>
    /// 固定比例
    /// </summary>
    FixedRatio,

    /// <summary>
    /// 等权
    /// </summary>
    EqualWeight
}
