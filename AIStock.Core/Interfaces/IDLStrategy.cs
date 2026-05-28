using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 深度学习策略接口
/// </summary>
public interface IDLStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 预测未来价格
    /// </summary>
    Task<PricePrediction> PredictAsync(string code, List<KlineData> klines, int predictDays = 5);

    /// <summary>
    /// 训练模型
    /// </summary>
    Task<bool> TrainAsync(string code, List<KlineData> klines);

    /// <summary>
    /// 检查模型是否已训练
    /// </summary>
    Task<bool> IsModelReadyAsync(string code);
}

/// <summary>
/// 价格预测结果
/// </summary>
public class PricePrediction
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 预测时间
    /// </summary>
    public DateTime PredictTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 当前价格
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 预测价格列表
    /// </summary>
    public List<PredictedPrice> Predictions { get; set; } = new();

    /// <summary>
    /// 预测趋势（up/down/sideways）
    /// </summary>
    public string Trend { get; set; } = "sideways";

    /// <summary>
    /// 置信度（0-100）
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// 模型名称
    /// </summary>
    public string ModelName { get; set; } = string.Empty;
}

/// <summary>
/// 预测价格
/// </summary>
public class PredictedPrice
{
    /// <summary>
    /// 预测日期
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 预测价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 预测上限
    /// </summary>
    public decimal UpperBound { get; set; }

    /// <summary>
    /// 预测下限
    /// </summary>
    public decimal LowerBound { get; set; }
}
