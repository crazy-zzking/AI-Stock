using System.Text.Json;

namespace AIStock.Intelligence.Common;

/// <summary>
/// LLM响应解析工具类
/// </summary>
public static class LLMResponseParser
{
    /// <summary>
    /// 清理LLM响应中的Markdown代码块标记
    /// </summary>
    public static string CleanJsonResponse(string response)
    {
        var jsonContent = response.Trim();
        if (jsonContent.StartsWith("```json"))
        {
            jsonContent = jsonContent[7..];
        }
        if (jsonContent.StartsWith("```"))
        {
            jsonContent = jsonContent[3..];
        }
        if (jsonContent.EndsWith("```"))
        {
            jsonContent = jsonContent[..^3];
        }
        return jsonContent.Trim();
    }

    /// <summary>
    /// 从JsonElement中获取字符串列表
    /// </summary>
    public static List<string> GetStringList(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array))
            return new List<string>();

        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrEmpty(value))
                result.Add(value);
        }
        return result;
    }

    /// <summary>
    /// 安全获取decimal值
    /// </summary>
    public static decimal GetDecimal(JsonElement element, string propertyName, decimal defaultValue = 0)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(prop.GetString(), out var val) ? val : defaultValue,
            _ => defaultValue
        };
    }

    /// <summary>
    /// 安全获取string值
    /// </summary>
    public static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    /// <summary>
    /// 安全获取int值
    /// </summary>
    public static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String => int.TryParse(prop.GetString(), out var val) ? val : defaultValue,
            _ => defaultValue
        };
    }

    /// <summary>
    /// 解析LLM JSON响应
    /// </summary>
    public static JsonElement? ParseJsonResponse(string response)
    {
        try
        {
            var jsonContent = CleanJsonResponse(response);
            var doc = JsonDocument.Parse(jsonContent);
            return doc.RootElement;
        }
        catch
        {
            return null;
        }
    }
}
