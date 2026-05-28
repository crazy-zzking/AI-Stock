using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIStock.Memory.Services;

/// <summary>
/// Agent记忆服务 — EF Core持久化实现
/// </summary>
public class AgentMemoryService : IAgentMemory
{
    private readonly AIStockDbContext _dbContext;
    private readonly ILogger<AgentMemoryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AgentMemoryService(
        AIStockDbContext dbContext,
        ILogger<AgentMemoryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SaveAsync(string agentId, AgentTask task, AgentResult result)
    {
        try
        {
            var stockCode = task.Parameters.GetValueOrDefault("code")?.ToString() ?? "";

            // 提取关键指标
            var keyMetrics = ExtractKeyMetrics(result);

            var entity = new AgentMemoryEntity
            {
                AgentId = agentId,
                TaskType = task.TaskType,
                StockCode = stockCode,
                Input = JsonSerializer.Serialize(task.Parameters, JsonOptions),
                Output = JsonSerializer.Serialize(result.Output, JsonOptions),
                KeyMetrics = keyMetrics != null ? JsonSerializer.Serialize(keyMetrics, JsonOptions) : null,
                Success = result.Success,
                Message = result.Message,
                ExecutionTimeMs = result.ExecutionTime,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AgentMemory.Add(entity);
            await _dbContext.SaveChangesAsync();

            _logger.LogDebug("Saved memory for agent {AgentId}, stock {Code}, task {TaskType}",
                agentId, stockCode, task.TaskType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save agent memory for {AgentId}", agentId);
        }
    }

    public async Task<List<AgentMemoryRecord>> GetHistoryAsync(string agentId, string stockCode, int count = 10)
    {
        var entities = await _dbContext.AgentMemory
            .Where(m => m.AgentId == agentId && m.StockCode == stockCode)
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .ToListAsync();

        return entities.Select(MapToRecord).ToList();
    }

    public async Task<AgentMemoryRecord?> GetLatestAsync(string agentId, string stockCode)
    {
        var entity = await _dbContext.AgentMemory
            .Where(m => m.AgentId == agentId && m.StockCode == stockCode)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        return entity != null ? MapToRecord(entity) : null;
    }

    public async Task<List<AgentMemoryRecord>> GetRecentAsync(string agentId, int count = 20)
    {
        var entities = await _dbContext.AgentMemory
            .Where(m => m.AgentId == agentId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .ToListAsync();

        return entities.Select(MapToRecord).ToList();
    }

    private static object? ExtractKeyMetrics(AgentResult result)
    {
        if (result.Output.Count == 0)
            return null;

        // 提取数值型指标作为关键度量
        var metrics = new Dictionary<string, decimal>();
        foreach (var (key, value) in result.Output)
        {
            if (value is decimal d)
                metrics[key] = d;
            else if (value is int i)
                metrics[key] = i;
            else if (value is long l)
                metrics[key] = l;
            else if (value is double db)
                metrics[key] = (decimal)db;
        }

        return metrics.Count > 0 ? metrics : null;
    }

    private static AgentMemoryRecord MapToRecord(AgentMemoryEntity entity)
    {
        return new AgentMemoryRecord
        {
            Id = entity.Id,
            AgentId = entity.AgentId,
            TaskType = entity.TaskType,
            StockCode = entity.StockCode,
            Input = entity.Input,
            Output = entity.Output,
            KeyMetrics = entity.KeyMetrics,
            Success = entity.Success,
            Message = entity.Message,
            ExecutionTimeMs = entity.ExecutionTimeMs,
            CreatedAt = entity.CreatedAt
        };
    }
}
