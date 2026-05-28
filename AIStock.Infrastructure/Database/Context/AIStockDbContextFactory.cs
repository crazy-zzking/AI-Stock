using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIStock.Infrastructure.Database.Context;

/// <summary>
/// 设计时DbContext工厂（用于EF Core Migration生成）
/// </summary>
public class AIStockDbContextFactory : IDesignTimeDbContextFactory<AIStockDbContext>
{
    public AIStockDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AIStockDbContext>();
        
        // 使用构建参数或默认连接字符串（仅用于Migration生成，不实际连接数据库）
        var connectionString = args.Length > 0 
            ? args[0] 
            : "Server=localhost;Port=3306;Database=aistock;User=root;Password=temp;";

        optionsBuilder.UseMySql(connectionString, ServerVersion.Parse("8.0.0"));

        return new AIStockDbContext(optionsBuilder.Options);
    }
}
