namespace AIStock.Core.Models;

/// <summary>
/// 数据源信息
/// </summary>
public class DataSourceInfo
{
    /// <summary>
    /// 数据源类型（database/api/llm/crawler）
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 数据源名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 查询条件/参数
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// 数据条数
    /// </summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// 数据时间范围
    /// </summary>
    public DateTime? DataTimeStart { get; set; }

    /// <summary>
    /// 数据时间范围
    /// </summary>
    public DateTime? DataTimeEnd { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 分析结果基类
/// </summary>
public class AnalysisResultBase
{
    /// <summary>
    /// 数据源列表
    /// </summary>
    public List<DataSourceInfo> DataSources { get; set; } = new();

    /// <summary>
    /// 分析时间
    /// </summary>
    public DateTime AnalysisTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 添加数据源
    /// </summary>
    public void AddDataSource(string type, string name, string? query = null, int recordCount = 0)
    {
        DataSources.Add(new DataSourceInfo
        {
            Type = type,
            Name = name,
            Query = query,
            RecordCount = recordCount
        });
    }
}
