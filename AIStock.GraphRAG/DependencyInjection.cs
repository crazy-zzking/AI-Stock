using AIStock.GraphRAG.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIStock.GraphRAG;

/// <summary>
/// GraphRAG模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加图谱增强RAG服务
    /// </summary>
    public static IServiceCollection AddGraphRAGServices(this IServiceCollection services)
    {
        services.TryAddScoped<IGraphRAGService, GraphRAGService>();
        return services;
    }
}
