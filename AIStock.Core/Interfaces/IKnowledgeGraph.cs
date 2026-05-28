using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 公司关系图谱接口
/// </summary>
public interface ICompanyRelationGraph
{
    /// <summary>
    /// 添加公司关系
    /// </summary>
    Task<long> AddRelationAsync(CompanyRelation relation);

    /// <summary>
    /// 批量添加公司关系
    /// </summary>
    Task<int> AddRelationsAsync(IEnumerable<CompanyRelation> relations);

    /// <summary>
    /// 获取公司的所有关系
    /// </summary>
    Task<List<CompanyRelation>> GetCompanyRelationsAsync(string companyCode);

    /// <summary>
    /// 获取公司的供应商
    /// </summary>
    Task<List<CompanyRelation>> GetSuppliersAsync(string companyCode);

    /// <summary>
    /// 获取公司的客户
    /// </summary>
    Task<List<CompanyRelation>> GetCustomersAsync(string companyCode);

    /// <summary>
    /// 获取公司的投资关系
    /// </summary>
    Task<List<CompanyRelation>> GetInvestmentsAsync(string companyCode);

    /// <summary>
    /// 获取公司的控股关系
    /// </summary>
    Task<List<CompanyRelation>> GetControllingAsync(string companyCode);

    /// <summary>
    /// 查找两个公司之间的关系路径
    /// </summary>
    Task<List<List<CompanyRelation>>> FindRelationPathAsync(string fromCode, string toCode, int maxDepth = 3);

    /// <summary>
    /// 删除公司关系
    /// </summary>
    Task<bool> DeleteRelationAsync(long id);
}

/// <summary>
/// 产业链图谱接口
/// </summary>
public interface IIndustryChainGraph
{
    /// <summary>
    /// 添加产业链节点
    /// </summary>
    Task<long> AddChainNodeAsync(IndustryChainNode node);

    /// <summary>
    /// 添加公司-产业链关联
    /// </summary>
    Task<long> AddCompanyChainRelationAsync(CompanyChainRelation relation);

    /// <summary>
    /// 获取产业链结构
    /// </summary>
    Task<List<IndustryChainNode>> GetChainStructureAsync(string chainName);

    /// <summary>
    /// 获取公司的产业链位置
    /// </summary>
    Task<List<CompanyChainRelation>> GetCompanyChainPositionsAsync(string companyCode);

    /// <summary>
    /// 获取产业链的上下游公司
    /// </summary>
    Task<List<CompanyChainRelation>> GetChainCompaniesAsync(string chainName, string? role = null);

    /// <summary>
    /// 获取所有产业链列表
    /// </summary>
    Task<List<string>> GetAllChainNamesAsync();

    /// <summary>
    /// 概念扩散推演
    /// </summary>
    Task<ConceptDiffusionResult> DiffuseConceptAsync(string coreEvent, List<string> relatedConcepts);
}

/// <summary>
/// 标的筛选器接口
/// </summary>
public interface IStockFilter
{
    /// <summary>
    /// 根据条件筛选标的
    /// </summary>
    Task<List<StockFilterResult>> FilterStocksAsync(StockFilterCriteria criteria);

    /// <summary>
    /// 根据概念筛选标的
    /// </summary>
    Task<List<StockFilterResult>> FilterByConceptsAsync(List<string> concepts, int count = 20);

    /// <summary>
    /// 根据产业链筛选标的
    /// </summary>
    Task<List<StockFilterResult>> FilterByChainAsync(string chainName, string? role = null, int count = 20);
}
