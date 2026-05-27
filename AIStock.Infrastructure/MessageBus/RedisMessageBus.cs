using System.Text.Json;
using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AIStock.Infrastructure.MessageBus;

/// <summary>
/// Redis Stream消息总线实现
/// </summary>
public class RedisMessageBus : IMessageBus, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisMessageBus> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public RedisMessageBus(IConnectionMultiplexer redis, ILogger<RedisMessageBus> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// 发布消息
    /// </summary>
    public async Task<string> PublishAsync<T>(string stream, T message, string messageId = "*")
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            var entries = new NameValueEntry[]
            {
                new NameValueEntry("data", json),
                new NameValueEntry("type", typeof(T).Name),
                new NameValueEntry("timestamp", DateTime.UtcNow.ToString("O"))
            };

            var id = await db.StreamAddAsync(stream, entries, messageId);
            _logger.LogDebug("Published message to stream {Stream}, ID: {Id}", stream, id);
            return id.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to stream {Stream}", stream);
            throw;
        }
    }

    /// <summary>
    /// 订阅消息
    /// </summary>
    public async Task SubscribeAsync<T>(string stream, string group, string consumer, Func<T, Task> handler, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // 确保消费者组存在
        await CreateConsumerGroupAsync(stream, group);

        _logger.LogInformation("Starting consumer {Consumer} in group {Group} for stream {Stream}", consumer, group, stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(stream, group, consumer, count: 10, noAck: false);

                if (entries.Length == 0)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    try
                    {
                        var data = entry["data"];
                        var message = JsonSerializer.Deserialize<T>(data!, _jsonOptions);

                        if (message != null)
                        {
                            await handler(message);
                            await db.StreamAcknowledgeAsync(stream, group, entry.Id);
                            _logger.LogDebug("Processed message {Id} from stream {Stream}", entry.Id, stream);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process message {Id} from stream {Stream}", entry.Id, stream);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from stream {Stream}", stream);
                await Task.Delay(1000, cancellationToken);
            }
        }

        _logger.LogInformation("Consumer {Consumer} stopped for stream {Stream}", consumer, stream);
    }

    /// <summary>
    /// 创建消费者组
    /// </summary>
    public async Task<bool> CreateConsumerGroupAsync(string stream, string group)
    {
        try
        {
            var db = _redis.GetDatabase();

            // 检查流是否存在，不存在则创建
            var streamExists = await db.KeyExistsAsync(stream);
            if (!streamExists)
            {
                // 创建空流
                await db.StreamAddAsync(stream, "init", "init");
                await db.StreamTrimAsync(stream, 0);
            }

            // 创建消费者组
            try
            {
                await db.StreamCreateConsumerGroupAsync(stream, group, StreamPosition.NewMessages);
                _logger.LogInformation("Created consumer group {Group} for stream {Stream}", group, stream);
                return true;
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // 消费者组已存在
                _logger.LogDebug("Consumer group {Group} already exists for stream {Stream}", group, stream);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create consumer group {Group} for stream {Stream}", group, stream);
            return false;
        }
    }

    /// <summary>
    /// 确认消息
    /// </summary>
    public async Task<bool> AcknowledgeAsync(string stream, string group, string messageId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var result = await db.StreamAcknowledgeAsync(stream, group, messageId);
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge message {Id} in stream {Stream}", messageId, stream);
            return false;
        }
    }

    /// <summary>
    /// 获取流信息
    /// </summary>
    public async Task<AIStock.Core.Interfaces.StreamInfo> GetStreamInfoAsync(string stream)
    {
        try
        {
            var db = _redis.GetDatabase();
            var info = await db.StreamInfoAsync(stream);

            return new AIStock.Core.Interfaces.StreamInfo
            {
                Stream = stream,
                Length = info.Length,
                FirstEntryId = info.FirstEntry.Id.ToString(),
                LastEntryId = info.LastEntry.Id.ToString(),
                ConsumerGroupCount = info.ConsumerGroupCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get info for stream {Stream}", stream);
            throw;
        }
    }

    /// <summary>
    /// 删除消息
    /// </summary>
    public async Task<long> DeleteMessagesAsync(string stream, params string[] messageIds)
    {
        try
        {
            var db = _redis.GetDatabase();
            var ids = messageIds.Select(id => (RedisValue)id).ToArray();
            return await db.StreamDeleteAsync(stream, ids);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete messages from stream {Stream}", stream);
            throw;
        }
    }

    /// <summary>
    /// 修剪流
    /// </summary>
    public async Task<long> TrimStreamAsync(string stream, long maxLength)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.StreamTrimAsync(stream, (int)maxLength);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trim stream {Stream}", stream);
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
