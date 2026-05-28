using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIStock.GraphRAG.Services;

/// <summary>
/// 图谱增强RAG服务实现 — 查询知识图谱，构建LLM可读的上下文文本
/// </summary>
public class GraphRAGService : IGraphRAGService
{
    private readonly ICompanyRelationGraph _companyGraph;
    private readonly IIndustryChainGraph _industryGraph;
    private readonly ILogger<GraphRAGService> _logger;

    public GraphRAGService(
        ICompanyRelationGraph companyGraph,
        IIndustryChainGraph industryGraph,
        ILogger<GraphRAGService> logger)
    {
        _companyGraph = companyGraph;
        _industryGraph = industryGraph;
        _logger = logger;
    }

    public async Task<GraphContext> BuildContextAsync(string code, string? queryType = null)
    {
        var context = new GraphContext();

        try
        {
            // 并行查询多维度图谱数据
            var suppliersTask = _companyGraph.GetSuppliersAsync(code);
            var customersTask = _companyGraph.GetCustomersAsync(code);
            var investmentsTask = _companyGraph.GetInvestmentsAsync(code);
            var controllingTask = _companyGraph.GetControllingAsync(code);
            var chainPositionsTask = _industryGraph.GetCompanyChainPositionsAsync(code);

            await Task.WhenAll(suppliersTask, customersTask, investmentsTask,
                controllingTask, chainPositionsTask);

            var suppliers = suppliersTask.Result;
            var customers = customersTask.Result;
            var investments = investmentsTask.Result;
            var controlling = controllingTask.Result;
            var chainPositions = chainPositionsTask.Result;

            // 构建供应商描述
            if (suppliers.Count > 0)
            {
                var supplierList = suppliers.Take(5).Select(s =>
                    $"{s.TargetCompany}({s.RelationType})").ToList();
                context.Suppliers = string.Join("、", supplierList);
            }

            // 构建客户描述
            if (customers.Count > 0)
            {
                var customerList = customers.Take(5).Select(c =>
                    $"{c.TargetCompany}({c.RelationType})").ToList();
                context.Customers = string.Join("、", customerList);
            }

            // 构建产业链描述
            if (chainPositions.Count > 0)
            {
                var chainDescriptions = chainPositions
                    .GroupBy(p => p.ChainName)
                    .Select(g => $"{g.Key}(角色：{string.Join("/", g.Select(p => p.Role).Distinct())})");
                context.IndustryChain = string.Join("; ", chainDescriptions);
            }

            // 投资和控股关系
            var relationSummaries = new List<string>();
            if (investments.Count > 0)
                relationSummaries.Add($"投资{investments.Count}家公司");
            if (controlling.Count > 0)
                relationSummaries.Add($"控股{controlling.Count}家公司");
            if (relationSummaries.Count > 0)
                context.CompanyInfo = string.Join("，", relationSummaries);

            _logger.LogDebug("Built graph context for {Code}: {SupplierCount} suppliers, {CustomerCount} customers, {ChainCount} chains",
                code, suppliers.Count, customers.Count, chainPositions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build graph context for {Code}", code);
        }

        return context;
    }
}
