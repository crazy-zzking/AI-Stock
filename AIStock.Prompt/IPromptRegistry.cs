using AIStock.Core.Models;
using AIStock.Prompt.Models;

namespace AIStock.Prompt;

/// <summary>
/// Prompt注册中心接口 — 管理Prompt模板的加载、版本控制和请求构建
/// </summary>
public interface IPromptRegistry
{
    /// <summary>
    /// 获取指定名称和版本的Prompt模板
    /// </summary>
    Task<PromptTemplate?> GetPromptAsync(string name, string version = "latest");

    /// <summary>
    /// 根据模板构建LLMRequest，自动填充变量
    /// </summary>
    Task<LLMRequest> BuildRequestAsync(string promptName, Dictionary<string, string> variables, string? version = null);

    /// <summary>
    /// 列出所有Prompt（可按分类过滤）
    /// </summary>
    Task<List<PromptInfo>> ListPromptsAsync(string? category = null);

    /// <summary>
    /// 重新加载所有Prompt（刷新缓存）
    /// </summary>
    Task ReloadAsync();

    /// <summary>
    /// 保存Prompt模板到数据库
    /// </summary>
    Task SavePromptAsync(PromptTemplate template);

    /// <summary>
    /// 删除Prompt模板
    /// </summary>
    /// <returns>是否删除成功</returns>
    Task<bool> DeletePromptAsync(string name, string version);

    /// <summary>
    /// 初始化默认Prompt到数据库（仅当表为空时执行）
    /// </summary>
    Task SeedDefaultPromptsAsync();
}

/// <summary>
/// Prompt简要信息
/// </summary>
public class PromptInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
}
