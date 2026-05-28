using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIStock.Web.Controllers;

/// <summary>
/// 知识图谱
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class KnowledgeController : ControllerBase
{
    private readonly ICompanyRelationGraph _companyGraph;
    private readonly IIndustryChainGraph _chainGraph;
    private readonly IStockFilter _stockFilter;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(
        ICompanyRelationGraph companyGraph,
        IIndustryChainGraph chainGraph,
        IStockFilter stockFilter,
        ILogger<KnowledgeController> logger)
    {
        _companyGraph = companyGraph;
        _chainGraph = chainGraph;
        _stockFilter = stockFilter;
        _logger = logger;
    }

    #region 公司关系图谱

    /// <summary>
    /// 添加公司关系
    /// </summary>
    [HttpPost("company/relation")]
    public async Task<IActionResult> AddCompanyRelation([FromBody] CompanyRelation relation)
    {
        var id = await _companyGraph.AddRelationAsync(relation);
        return Ok(new { id });
    }

    /// <summary>
    /// 批量添加公司关系
    /// </summary>
    [HttpPost("company/relations")]
    public async Task<IActionResult> AddCompanyRelations([FromBody] List<CompanyRelation> relations)
    {
        var count = await _companyGraph.AddRelationsAsync(relations);
        return Ok(new { count });
    }

    /// <summary>
    /// 获取公司的所有关系
    /// </summary>
    [HttpGet("company/{companyCode}/relations")]
    public async Task<IActionResult> GetCompanyRelations(string companyCode)
    {
        var relations = await _companyGraph.GetCompanyRelationsAsync(companyCode);
        return Ok(relations);
    }

    /// <summary>
    /// 获取公司的供应商
    /// </summary>
    [HttpGet("company/{companyCode}/suppliers")]
    public async Task<IActionResult> GetSuppliers(string companyCode)
    {
        var suppliers = await _companyGraph.GetSuppliersAsync(companyCode);
        return Ok(suppliers);
    }

    /// <summary>
    /// 获取公司的客户
    /// </summary>
    [HttpGet("company/{companyCode}/customers")]
    public async Task<IActionResult> GetCustomers(string companyCode)
    {
        var customers = await _companyGraph.GetCustomersAsync(companyCode);
        return Ok(customers);
    }

    /// <summary>
    /// 查找两个公司之间的关系路径
    /// </summary>
    [HttpGet("company/path")]
    public async Task<IActionResult> FindRelationPath([FromQuery] string from, [FromQuery] string to, [FromQuery] int maxDepth = 3)
    {
        var paths = await _companyGraph.FindRelationPathAsync(from, to, maxDepth);
        return Ok(paths);
    }

    #endregion

    #region 产业链图谱

    /// <summary>
    /// 添加产业链节点
    /// </summary>
    [HttpPost("chain/node")]
    public async Task<IActionResult> AddChainNode([FromBody] IndustryChainNode node)
    {
        var id = await _chainGraph.AddChainNodeAsync(node);
        return Ok(new { id });
    }

    /// <summary>
    /// 添加公司-产业链关联
    /// </summary>
    [HttpPost("chain/company")]
    public async Task<IActionResult> AddCompanyChainRelation([FromBody] CompanyChainRelation relation)
    {
        var id = await _chainGraph.AddCompanyChainRelationAsync(relation);
        return Ok(new { id });
    }

    /// <summary>
    /// 获取产业链结构
    /// </summary>
    [HttpGet("chain/{chainName}")]
    public async Task<IActionResult> GetChainStructure(string chainName)
    {
        var structure = await _chainGraph.GetChainStructureAsync(chainName);
        return Ok(structure);
    }

    /// <summary>
    /// 获取公司的产业链位置
    /// </summary>
    [HttpGet("chain/company/{companyCode}")]
    public async Task<IActionResult> GetCompanyChainPositions(string companyCode)
    {
        var positions = await _chainGraph.GetCompanyChainPositionsAsync(companyCode);
        return Ok(positions);
    }

    /// <summary>
    /// 获取产业链的上下游公司
    /// </summary>
    [HttpGet("chain/{chainName}/companies")]
    public async Task<IActionResult> GetChainCompanies(string chainName, [FromQuery] string? role = null)
    {
        var companies = await _chainGraph.GetChainCompaniesAsync(chainName, role);
        return Ok(companies);
    }

    /// <summary>
    /// 获取所有产业链列表
    /// </summary>
    [HttpGet("chains")]
    public async Task<IActionResult> GetAllChainNames()
    {
        var chains = await _chainGraph.GetAllChainNamesAsync();
        return Ok(chains);
    }

    /// <summary>
    /// 概念扩散推演
    /// </summary>
    [HttpPost("chain/diffuse")]
    public async Task<IActionResult> DiffuseConcept([FromBody] ConceptDiffusionRequest request)
    {
        var result = await _chainGraph.DiffuseConceptAsync(request.CoreEvent, request.RelatedConcepts);
        return Ok(result);
    }

    #endregion

    #region 标的筛选

    /// <summary>
    /// 根据条件筛选标的
    /// </summary>
    [HttpPost("filter")]
    public async Task<IActionResult> FilterStocks([FromBody] StockFilterCriteria criteria)
    {
        var results = await _stockFilter.FilterStocksAsync(criteria);
        return Ok(results);
    }

    /// <summary>
    /// 根据概念筛选标的
    /// </summary>
    [HttpPost("filter/concepts")]
    public async Task<IActionResult> FilterByConcepts([FromBody] ConceptFilterRequest request)
    {
        var results = await _stockFilter.FilterByConceptsAsync(request.Concepts, request.Count);
        return Ok(results);
    }

    /// <summary>
    /// 根据产业链筛选标的
    /// </summary>
    [HttpPost("filter/chain")]
    public async Task<IActionResult> FilterByChain([FromBody] ChainFilterRequest request)
    {
        var results = await _stockFilter.FilterByChainAsync(request.ChainName, request.Role, request.Count);
        return Ok(results);
    }

    #endregion
}

/// <summary>
/// 概念扩散请求
/// </summary>
public class ConceptDiffusionRequest
{
    public string CoreEvent { get; set; } = string.Empty;
    public List<string> RelatedConcepts { get; set; } = new();
}

/// <summary>
/// 概念筛选请求
/// </summary>
public class ConceptFilterRequest
{
    public List<string> Concepts { get; set; } = new();
    public int Count { get; set; } = 20;
}

/// <summary>
/// 产业链筛选请求
/// </summary>
public class ChainFilterRequest
{
    public string ChainName { get; set; } = string.Empty;
    public string? Role { get; set; }
    public int Count { get; set; } = 20;
}
