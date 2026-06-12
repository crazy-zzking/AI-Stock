using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Execution.Services;

/// <summary>
/// 交易闸门状态的 MySQL 持久化（trading_gate_state 单行表）。
/// 所有操作自吞异常：存储故障时闸门退化为纯内存行为并告警，绝不阻塞下单链路。
/// </summary>
public class DbTradingGateStore : ITradingGateStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DbTradingGateStore> _logger;

    public DbTradingGateStore(IServiceScopeFactory scopeFactory, ILogger<DbTradingGateStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public TradingGateState? Load()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var row = db.TradingGateState.Find(1);
            if (row == null) return null;

            var suspensions = new Dictionary<string, DateTime>();
            try
            {
                suspensions = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(row.SuspensionsJson) ?? new();
            }
            catch (JsonException) { /* 脏数据按空名单处理 */ }

            return new TradingGateState
            {
                Halted = row.Halted,
                HaltReason = row.HaltReason,
                CountDate = DateOnly.FromDateTime(row.CountDate),
                OrderCount = row.OrderCount,
                StockSuspensions = suspensions,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "交易闸门状态加载失败，按初始状态运行（存储降级）");
            return null;
        }
    }

    public void Save(TradingGateState state)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var row = db.TradingGateState.Find(1);
            if (row == null)
            {
                row = new TradingGateStateEntity { Id = 1 };
                db.TradingGateState.Add(row);
            }
            row.Halted = state.Halted;
            row.HaltReason = state.HaltReason;
            row.CountDate = state.CountDate.ToDateTime(TimeOnly.MinValue);
            row.OrderCount = state.OrderCount;
            row.SuspensionsJson = JsonSerializer.Serialize(state.StockSuspensions);
            row.UpdatedAt = DateTime.Now;
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "交易闸门状态保存失败（存储降级，内存状态不受影响）");
        }
    }
}
