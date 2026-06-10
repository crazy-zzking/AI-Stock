using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace AIStock.Core.Json;

/// <summary>
/// 全局共享的 JSON 序列化选项。统一入库 JSON（results_json/config_json/definition_json 等）的口径：
/// 中文原样存储（不转义成 \uXXXX，翻库可读），仍转义 &lt;&gt;&amp; 等防注入字符；反序列化大小写不敏感。
/// </summary>
public static class AppJson
{
    /// <summary>中文不转义的编码器（仍转义 &lt;&gt;&amp; 等）。供各处本地 JsonSerializerOptions 复用。</summary>
    public static readonly JavaScriptEncoder CjkEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    /// <summary>默认选项：中文不转义 + 属性名大小写不敏感。</summary>
    public static readonly JsonSerializerOptions Default = new()  
    {
        Encoder = CjkEncoder,
        PropertyNameCaseInsensitive = true,
    };
}
