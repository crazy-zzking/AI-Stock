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
    /// <param name="name">Prompt名称（不含版本后缀）</param>
    /// <param name="version">版本号，默认"latest"取最新版本</param>
    PromptTemplate? GetPrompt(string name, string version = "latest");

    /// <summary>
    /// 根据模板构建LLMRequest，自动填充变量
    /// </summary>
    LLMRequest BuildRequest(string promptName, Dictionary<string, string> variables, string? version = null);

    /// <summary>
    /// 列出所有Prompt（可按分类过滤）
    /// </summary>
    List<PromptInfo> ListPrompts(string? category = null);

    /// <summary>
    /// 重新加载所有Prompt（热更新）
    /// </summary>
    Task ReloadAsync();
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
