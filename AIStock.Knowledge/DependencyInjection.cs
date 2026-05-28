using AIStock.Core.Interfaces;
using AIStock.Knowledge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Knowledge;

/// <summary>
/// 知识图谱模块依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 添加知识图谱服务
    /// </summary>
    public static IServiceCollection AddKnowledgeServices(this IServiceCollection services)
    {
        services.AddScoped<ICompanyRelationGraph, CompanyRelationGraphService>();
        services.AddScoped<IIndustryChainGraph, IndustryChainGraphService>();
        services.AddScoped<IStockFilter, StockFilterService>();
        return services;
    }
}
