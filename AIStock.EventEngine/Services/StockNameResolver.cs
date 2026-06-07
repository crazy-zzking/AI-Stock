using System.Text.RegularExpressions;

namespace AIStock.EventEngine.Services;

/// <summary>
/// 事件关联个股的"名称 → 代码"归一（纯函数，便于单测）。
/// 解决采集/LLM 抽取出的股票标识可能是残缺名（如"宁德"而非"宁德时代"）导致精确匹配丢失关联的问题。
/// 归一优先级：6 位代码直接用 → 全名精确匹配 → 模糊包含匹配（命中数 ≤ 歧义上限才归一，过泛则跳过避免错链）。
/// </summary>
public static class StockNameResolver
{
    private static readonly Regex CodeRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>模糊包含匹配的歧义上限：命中超过此数视为过泛（如"中国/科技"），跳过不猜。</summary>
    public const int DefaultFuzzyMaxAmbiguity = 3;

    /// <summary>模糊匹配的最小片段长度：短于此长度（如单字）不做模糊，避免无意义爆链。</summary>
    public const int MinFuzzyLength = 2;

    public readonly record struct Result(IReadOnlyList<string> Codes, IReadOnlyList<string> Unresolved);

    /// <summary>
    /// 把原始股票标识列表归一为去重的股票代码集合。
    /// </summary>
    /// <param name="rawStocks">原始标识（可能是 6 位代码、全名或残缺名）</param>
    /// <param name="universe">全市场个股 (名称, 代码)，用于精确/模糊匹配</param>
    /// <param name="fuzzyMaxAmbiguity">模糊包含命中数上限，超过则跳过</param>
    public static Result ResolveCodes(
        IEnumerable<string> rawStocks,
        IReadOnlyCollection<(string Name, string Code)> universe,
        int fuzzyMaxAmbiguity = DefaultFuzzyMaxAmbiguity)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();

        var cleaned = (rawStocks ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (cleaned.Count == 0) return new Result(Array.Empty<string>(), Array.Empty<string>());

        // 全名 → 代码（同名取首个）
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, code) in universe)
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code) && !byName.ContainsKey(name))
                byName[name] = code;

        foreach (var s in cleaned)
        {
            if (CodeRegex.IsMatch(s)) { codes.Add(s); continue; }      // 已是 6 位代码
            if (byName.TryGetValue(s, out var exact)) { codes.Add(exact); continue; } // 全名精确

            // 模糊包含：片段太短不猜
            if (s.Length < MinFuzzyLength) { unresolved.Add(s); continue; }
            var hits = universe
                .Where(u => !string.IsNullOrWhiteSpace(u.Code) && !string.IsNullOrEmpty(u.Name) && u.Name.Contains(s, StringComparison.Ordinal))
                .ToList();
            if (hits.Count == 0 || hits.Count > fuzzyMaxAmbiguity) { unresolved.Add(s); continue; } // 无命中 / 过泛
            foreach (var h in hits) codes.Add(h.Code);
        }

        return new Result(codes.ToList(), unresolved);
    }
}
