namespace AIStock.Intelligence;

/// <summary>
/// 知识星球采集配置。注意：知识星球对高频抓取会封号，务必低频 + 随机抖动。
/// </summary>
public class KnowledgeStarOptions
{
    public const string SectionName = "KnowledgeStar";

    /// <summary>zsxq access_token（浏览器登录后从 cookie 复制）</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>API 基址</summary>
    public string BaseUrl { get; set; } = "https://api.zsxq.com/v2";

    /// <summary>要采集的星球 group_id 列表</summary>
    public List<string> GroupIds { get; set; } = new();

    /// <summary>每个星球每次取的主题条数</summary>
    public int Count { get; set; } = 20;

    /// <summary>请求 User-Agent（建议用真实浏览器 UA）</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    /// <summary>防封：每次请求前的最小随机延迟（毫秒）</summary>
    public int MinDelayMs { get; set; } = 3000;

    /// <summary>防封：每次请求前的最大随机延迟（毫秒）</summary>
    public int MaxDelayMs { get; set; } = 8000;
}
