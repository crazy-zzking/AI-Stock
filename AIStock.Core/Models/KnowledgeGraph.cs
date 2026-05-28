namespace AIStock.Core.Models;

/// <summary>
/// 公司关系
/// </summary>
public class CompanyRelation
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 源公司代码
    /// </summary>
    public string SourceCompany { get; set; } = string.Empty;

    /// <summary>
    /// 源公司名称
    /// </summary>
    public string? SourceCompanyName { get; set; }

    /// <summary>
    /// 目标公司代码
    /// </summary>
    public string TargetCompany { get; set; } = string.Empty;

    /// <summary>
    /// 目标公司名称
    /// </summary>
    public string? TargetCompanyName { get; set; }

    /// <summary>
    /// 关系类型（customer/supplier/invest/control）
    /// </summary>
    public string RelationType { get; set; } = string.Empty;

    /// <summary>
    /// 权重
    /// </summary>
    public decimal Weight { get; set; } = 1.0m;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 产业链节点
/// </summary>
public class IndustryChainNode
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 产业链名称
    /// </summary>
    public string ChainName { get; set; } = string.Empty;

    /// <summary>
    /// 父节点
    /// </summary>
    public string? ParentNode { get; set; }

    /// <summary>
    /// 子节点
    /// </summary>
    public string ChildNode { get; set; } = string.Empty;

    /// <summary>
    /// 层级
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 节点类型（company/product/technology）
    /// </summary>
    public string? NodeType { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 公司-产业链关联
/// </summary>
public class CompanyChainRelation
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 公司代码
    /// </summary>
    public string CompanyCode { get; set; } = string.Empty;

    /// <summary>
    /// 公司名称
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// 产业链ID
    /// </summary>
    public long ChainId { get; set; }

    /// <summary>
    /// 产业链名称
    /// </summary>
    public string? ChainName { get; set; }

    /// <summary>
    /// 在产业链中的节点名称
    /// </summary>
    public string ChainNode { get; set; } = string.Empty;

    /// <summary>
    /// 角色（upstream/midstream/downstream）
    /// </summary>
    public string? Role { get; set; }
}

/// <summary>
/// 概念扩散结果
/// </summary>
public class ConceptDiffusionResult
{
    /// <summary>
    /// 核心事件
    /// </summary>
    public string CoreEvent { get; set; } = string.Empty;

    /// <summary>
    /// 关联产业链
    /// </summary>
    public List<string> RelatedChains { get; set; } = new();

    /// <summary>
    /// 受益公司列表
    /// </summary>
    public List<BenefitCompany> BenefitCompanies { get; set; } = new();

    /// <summary>
    /// 扩散路径
    /// </summary>
    public List<string> DiffusionPath { get; set; } = new();
}

/// <summary>
/// 受益公司
/// </summary>
public class BenefitCompany
{
    /// <summary>
    /// 公司代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 公司名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 受益原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 受益程度（1-10）
    /// </summary>
    public int BenefitLevel { get; set; }

    /// <summary>
    /// 在产业链中的角色
    /// </summary>
    public string? ChainRole { get; set; }
}

/// <summary>
/// 标的筛选条件
/// </summary>
public class StockFilterCriteria
{
    /// <summary>
    /// 最大市值（亿）
    /// </summary>
    public decimal? MaxMarketCap { get; set; }

    /// <summary>
    /// 最小市值（亿）
    /// </summary>
    public decimal? MinMarketCap { get; set; }

    /// <summary>
    /// 关联概念
    /// </summary>
    public List<string>? RelatedConcepts { get; set; }

    /// <summary>
    /// 关联行业
    /// </summary>
    public List<string>? RelatedIndustries { get; set; }

    /// <summary>
    /// 是否排除机构重仓股
    /// </summary>
    public bool ExcludeInstitutionHeavy { get; set; }

    /// <summary>
    /// 是否排除已启动股票
    /// </summary>
    public bool ExcludeStarted { get; set; }

    /// <summary>
    /// 最大涨幅限制（%）
    /// </summary>
    public decimal? MaxRisePercent { get; set; }

    /// <summary>
    /// 返回数量
    /// </summary>
    public int Count { get; set; } = 20;
}

/// <summary>
/// 筛选结果
/// </summary>
public class StockFilterResult
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 市值（亿）
    /// </summary>
    public decimal MarketCap { get; set; }

    /// <summary>
    /// 涨幅（%）
    /// </summary>
    public decimal RisePercent { get; set; }

    /// <summary>
    /// 关联概念
    /// </summary>
    public List<string> RelatedConcepts { get; set; } = new();

    /// <summary>
    /// 筛选得分
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// 筛选原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
