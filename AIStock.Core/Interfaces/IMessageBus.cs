namespace AIStock.Core.Interfaces;

/// <summary>
/// 消息总线接口
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// 发布消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="stream">流名称</param>
    /// <param name="message">消息内容</param>
    /// <param name="messageId">消息ID（可选，*表示自动生成）</param>
    /// <returns>消息ID</returns>
    Task<string> PublishAsync<T>(string stream, T message, string messageId = "*");

    /// <summary>
    /// 订阅消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="stream">流名称</param>
    /// <param name="group">消费者组</param>
    /// <param name="consumer">消费者名称</param>
    /// <param name="handler">消息处理器</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SubscribeAsync<T>(string stream, string group, string consumer, Func<T, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建消费者组
    /// </summary>
    /// <param name="stream">流名称</param>
    /// <param name="group">消费者组名称</param>
    /// <returns>是否创建成功</returns>
    Task<bool> CreateConsumerGroupAsync(string stream, string group);

    /// <summary>
    /// 确认消息
    /// </summary>
    /// <param name="stream">流名称</param>
    /// <param name="group">消费者组</param>
    /// <param name="messageId">消息ID</param>
    /// <returns>是否确认成功</returns>
    Task<bool> AcknowledgeAsync(string stream, string group, string messageId);

    /// <summary>
    /// 获取流信息
    /// </summary>
    /// <param name="stream">流名称</param>
    /// <returns>流信息</returns>
    Task<StreamInfo> GetStreamInfoAsync(string stream);

    /// <summary>
    /// 删除消息
    /// </summary>
    /// <param name="stream">流名称</param>
    /// <param name="messageIds">消息ID列表</param>
    /// <returns>删除的数量</returns>
    Task<long> DeleteMessagesAsync(string stream, params string[] messageIds);

    /// <summary>
    /// 修剪流（保留指定数量的消息）
    /// </summary>
    /// <param name="stream">流名称</param>
    /// <param name="maxLength">最大长度</param>
    /// <returns>修剪的数量</returns>
    Task<long> TrimStreamAsync(string stream, long maxLength);
}

/// <summary>
/// 流信息
/// </summary>
public class StreamInfo
{
    /// <summary>
    /// 流名称
    /// </summary>
    public string Stream { get; set; } = string.Empty;

    /// <summary>
    /// 长度
    /// </summary>
    public long Length { get; set; }

    /// <summary>
    /// 第一条消息ID
    /// </summary>
    public string? FirstEntryId { get; set; }

    /// <summary>
    /// 最后一条消息ID
    /// </summary>
    public string? LastEntryId { get; set; }

    /// <summary>
    /// 消费者组数量
    /// </summary>
    public int ConsumerGroupCount { get; set; }
}
