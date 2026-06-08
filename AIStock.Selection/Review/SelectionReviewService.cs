using System.Text;
using System.Text.Json;
using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Selection.Review;

/// <summary>
/// 选股 LLM 复评服务。对某批选股结果的前 N 只做：
/// ⑤组合视角(整批1次) → ④风险排雷 + ①核心逻辑叙述 + ②情报印证(逐只1次)，
/// "建议买入"的附规则算出的买入计划（价位规则算，LLM 仅解释）。
/// 复评不改选股结果（排序/入选），只把结果写回 ResultsJson 并置 review_status。
/// 仅依赖 DbContext + ILLMService 接口，不破坏选股主路"纯 DB"约定。
/// </summary>
public class SelectionReviewService
{
    private readonly AIStockDbContext _db;
    private readonly ILLMService _llm;
    private readonly SelectionReviewOptions _options;
    private readonly ILogger<SelectionReviewService> _logger;

    public SelectionReviewService(
        AIStockDbContext db,
        ILLMService llm,
        IOptions<SelectionReviewOptions> options,
        ILogger<SelectionReviewService> logger)
    {
        _db = db;
        _llm = llm;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 复评指定批次（按 selection_result.id）。幂等：done 的批次直接跳过。
    /// </summary>
    public async Task ReviewBatchAsync(long selectionResultId, CancellationToken ct = default)
    {
        var row = await _db.SelectionResult.FirstOrDefaultAsync(r => r.Id == selectionResultId, ct);
        if (row == null)
        {
            _logger.LogWarning("复评跳过：选股批次 {Id} 不存在", selectionResultId);
            return;
        }
        if (row.ReviewStatus == "done")
        {
            _logger.LogInformation("复评跳过：批次 {Id} 已完成", selectionResultId);
            return;
        }
        if (!_options.Enabled)
        {
            row.ReviewStatus = "skipped";
            await _db.SaveChangesAsync(ct);
            return;
        }

        var all = JsonSerializer.Deserialize<List<StockSelectionResult>>(row.ResultsJson) ?? new();
        if (all.Count == 0)
        {
            row.ReviewStatus = "done";
            row.ReviewedAt = DateTime.Now;
            await _db.SaveChangesAsync(ct);
            return;
        }

        row.ReviewStatus = "running";
        await _db.SaveChangesAsync(ct);

        var topN = Math.Clamp(_options.TopN, 1, all.Count);
        var targets = all.Take(topN).ToList();
        _logger.LogInformation("选股复评开始：批次 {Id}，前 {N}/{Total} 只", selectionResultId, topN, all.Count);

        try
        {
            // 预取价位计算所需 K 线 + 情报
            var codes = targets.Select(t => t.Code).Distinct().ToList();
            var barsByCode = await LoadBarsAsync(codes, row.TradingDate.Date, ct);
            var eventsByCode = await LoadEventsAsync(codes, ct);

            // ⑤ 组合视角（整批 1 次）
            var overall = await ReviewOverallAsync(targets, ct);

            var ok = 0;
            foreach (var item in targets)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    barsByCode.TryGetValue(item.Code, out var bars);
                    eventsByCode.TryGetValue(item.Code, out var events);
                    var review = await ReviewOneAsync(item, bars, events, overall, ct);
                    if (review != null) { item.Review = review; ok++; }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "复评单只失败 {Code} {Name}", item.Code, item.Name);
                }
                if (_options.PerCallDelayMs > 0)
                    await Task.Delay(_options.PerCallDelayMs, ct);
            }

            // 回写（targets 是 all 前 N 个的同一引用，已就地赋值 Review）
            row.ResultsJson = JsonSerializer.Serialize(all);
            row.ReviewStatus = "done";
            row.ReviewedAt = DateTime.Now;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("选股复评完成：批次 {Id}，成功 {Ok}/{N}", selectionResultId, ok, topN);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选股复评失败：批次 {Id}", selectionResultId);
            try
            {
                row.ReviewStatus = "failed";
                await _db.SaveChangesAsync(ct);
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>⑤ 组合视角：把前 N 只的题材/行业/大盘环境整体丢给 LLM，得一段整体研判作上下文。</summary>
    private async Task<string> ReviewOverallAsync(List<StockSelectionResult> targets, CancellationToken ct)
    {
        var regime = targets.FirstOrDefault()?.MarketRegime ?? "";
        var concepts = targets.SelectMany(t => t.HotConcepts.Count > 0 ? t.HotConcepts : t.Concepts.Take(2))
            .GroupBy(c => c).OrderByDescending(g => g.Count())
            .Take(8).Select(g => $"{g.Key}×{g.Count()}").ToList();
        var industries = targets.Where(t => !string.IsNullOrEmpty(t.Industry))
            .GroupBy(t => t.Industry).OrderByDescending(g => g.Count())
            .Take(6).Select(g => $"{g.Key}×{g.Count()}").ToList();

        var prompt = new StringBuilder();
        prompt.AppendLine("以下是今日某选股策略选出的候选（已按分数排序）的整体画像：");
        prompt.AppendLine($"大盘环境：{regime}");
        prompt.AppendLine($"题材分布：{(concepts.Count > 0 ? string.Join("、", concepts) : "无明显集中")}");
        prompt.AppendLine($"行业分布：{(industries.Count > 0 ? string.Join("、", industries) : "分散")}");
        prompt.AppendLine();
        prompt.AppendLine("请用 2-3 句话点评：当前选股整体偏向什么风格/题材、集中度如何、给出整体参与建议（仓位/谨慎程度）。只输出点评文字，不要分点。");

        try
        {
            var resp = await SendAsync("你是资深A股短线交易策略分析师，判断客观、注重风险。", prompt.ToString(), ct);
            return resp?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "组合视角复评失败");
            return "";
        }
    }

    /// <summary>④①② 逐只复评：返回 LlmReview（含建议、风险、情报印证、叙述、买入计划）。</summary>
    private async Task<LlmReview?> ReviewOneAsync(
        StockSelectionResult s,
        List<PriceLevelCalculator.PriceBar>? bars,
        List<EventBrief>? events,
        string overall,
        CancellationToken ct)
    {
        var sys = "你是资深A股短线交易分析师。基于给定的量化因子与情报，对个股做复评：" +
                  "先排雷（识别高位追涨、解禁/减持、问询函、题材证伪等风险），再判断建议等级，" +
                  "并结合消息面印证。务必客观，宁可保守。只输出 JSON，不要多余文字。";

        var u = new StringBuilder();
        u.AppendLine($"股票：{s.Name}（{s.Code}） 行业：{s.Industry}");
        if (s.Concepts.Count > 0) u.AppendLine($"概念：{string.Join("、", s.Concepts.Take(6))}");
        if (s.HotConcepts.Count > 0) u.AppendLine($"命中当日热门题材：{string.Join("、", s.HotConcepts)}");
        u.AppendLine($"选股日收盘：{s.Close:F2}，当日涨幅：{s.ChangePercent:F2}%，20日涨幅：{s.Rise20d:F2}%");
        u.AppendLine($"市值：{s.TotalMarketCap / 1e8m:F1}亿，PE(TTM)：{s.PeTtm:F1}");
        u.AppendLine($"主力净流入：{s.MainNetInflow / 1e4m:F0}万，连续净流入：{s.ConsecutiveInflowDays}天，连板：{s.ConsecutiveLimitUp}");
        u.AppendLine($"综合评分：{s.TotalScore:F1}，评级：{s.RatingStars}星");
        if (!string.IsNullOrEmpty(s.CoreLogic)) u.AppendLine($"规则核心逻辑：{s.CoreLogic}");
        if (s.Tags.Count > 0) u.AppendLine($"标签：{string.Join("、", s.Tags)}");
        if (!string.IsNullOrEmpty(overall)) u.AppendLine($"今日大盘/组合研判：{overall}");

        if (events is { Count: > 0 })
        {
            u.AppendLine("相关情报（近期）：");
            foreach (var e in events)
                u.AppendLine($"- [{e.Source}/{e.Sentiment}/可信{e.Credibility}] {e.Title}");
        }
        else
        {
            u.AppendLine("相关情报：近期无匹配情报记录。");
        }

        u.AppendLine();
        u.AppendLine("请输出 JSON（字段固定）：");
        u.AppendLine("{");
        u.AppendLine("  \"recommendation\": \"buy|watch|avoid\",   // 建议买入/观望/回避");
        u.AppendLine("  \"confidence\": 0-100,                     // 置信度");
        u.AppendLine("  \"riskFlags\": [\"...\"],                  // 风险/排雷标签，无则空数组");
        u.AppendLine("  \"intelligenceNote\": \"...\",            // 消息面是支持还是证伪，一句话；无情报则说明");
        u.AppendLine("  \"narrative\": \"...\",                   // 核心逻辑/看点，一两句");
        u.AppendLine("  \"priceComment\": \"...\"                 // 仅 buy 时：对买入时机/价位的提示，可空");
        u.AppendLine("}");

        var content = await SendAsync(sys, u.ToString(), ct);
        if (string.IsNullOrWhiteSpace(content)) return null;

        var root = ParseJson(content);
        if (root == null) { _logger.LogDebugJson(s.Code, content); return null; }
        var el = root.Value;

        var rec = ParseRecommendation(GetString(el, "recommendation"));
        var review = new LlmReview
        {
            Recommendation = rec,
            Confidence = Math.Clamp(GetInt(el, "confidence"), 0, 100),
            RiskFlags = GetStringList(el, "riskFlags"),
            IntelligenceNote = GetString(el, "intelligenceNote") ?? "",
            Narrative = GetString(el, "narrative") ?? "",
            Model = _options.ModelId ?? "default",
            ReviewedAt = DateTime.Now,
        };

        // 仅"建议买入"给买入计划：价位规则算，LLM 解释追加到 Basis
        if (rec == ReviewRecommendation.Buy)
        {
            var plan = PriceLevelCalculator.Compute(bars ?? new List<PriceLevelCalculator.PriceBar>(), s.Close);
            var comment = GetString(el, "priceComment");
            if (!string.IsNullOrWhiteSpace(comment))
                plan.Basis = $"{plan.Basis}｜{comment.Trim()}";
            review.Plan = plan;
        }

        return review;
    }

    /// <summary>取各股近 N 天的情报事件（按 RelatedStocks 匹配代码）。</summary>
    private async Task<Dictionary<string, List<EventBrief>>> LoadEventsAsync(List<string> codes, CancellationToken ct)
    {
        var result = new Dictionary<string, List<EventBrief>>();
        var since = DateTime.Now.AddDays(-Math.Max(1, _options.IntelligenceLookbackDays));
        // 一次查出近 N 天、RelatedStocks 命中任一候选代码的事件，再在内存里按代码归类
        var rows = await _db.EventRecord
            .Where(e => e.EventTime != null && e.EventTime >= since
                        && e.RelatedStocks != null && e.RelatedStocks != "")
            .OrderByDescending(e => e.EventTime)
            .Select(e => new { e.Title, e.Source, e.Sentiment, e.Credibility, e.RelatedStocks })
            .Take(2000)
            .ToListAsync(ct);

        var max = Math.Max(1, _options.MaxIntelligencePerStock);
        foreach (var code in codes)
        {
            var hits = rows
                .Where(r => r.RelatedStocks!.Contains(code))
                .Take(max)
                .Select(r => new EventBrief(
                    r.Title ?? "", r.Source ?? "情报",
                    SentimentText(r.Sentiment), r.Credibility ?? 0))
                .ToList();
            if (hits.Count > 0) result[code] = hits;
        }
        return result;
    }

    /// <summary>取各股选股日(含)前约 30 自然日的日 K，供价位计算。</summary>
    private async Task<Dictionary<string, List<PriceLevelCalculator.PriceBar>>> LoadBarsAsync(
        List<string> codes, DateTime tradingDate, CancellationToken ct)
    {
        const string interval = nameof(KlineInterval.Daily);
        var since = tradingDate.AddDays(-30);
        var rows = await _db.KlineData
            .Where(k => k.Interval == interval && codes.Contains(k.Code)
                        && k.DateTime >= since && k.DateTime <= tradingDate)
            .Select(k => new { k.Code, k.DateTime, k.High, k.Low, k.Close })
            .ToListAsync(ct);

        return rows
            .GroupBy(k => k.Code)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.DateTime)
                      .Select(x => new PriceLevelCalculator.PriceBar(x.High, x.Low, x.Close))
                      .ToList());
    }

    /// <summary>统一发起 LLM 调用（按配置选模型）。</summary>
    private async Task<string?> SendAsync(string system, string user, CancellationToken ct)
    {
        var req = new LLMRequest { SystemPrompt = system, UserPrompt = user, Temperature = 0.3m };
        var resp = string.IsNullOrWhiteSpace(_options.ModelId)
            ? await _llm.SendAsync(req, ct)
            : await _llm.SendAsync(req, _options.ModelId!, ct);
        return resp.Success ? resp.Content : null;
    }

    // —— 小工具 ——

    private record EventBrief(string Title, string Source, string Sentiment, int Credibility);

    private static string SentimentText(string? s) => s switch
    {
        "positive" => "利好",
        "negative" => "利空",
        _ => "中性",
    };

    private static ReviewRecommendation ParseRecommendation(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "buy" or "建议买入" or "买入" => ReviewRecommendation.Buy,
        "avoid" or "回避" => ReviewRecommendation.Avoid,
        _ => ReviewRecommendation.Watch,
    };

    private static JsonElement? ParseJson(string response)
    {
        try
        {
            var json = CleanJson(response);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private static string CleanJson(string response)
    {
        var s = response.Trim();
        if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        else if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        s = s.Trim();
        // 容错：截取首个 { 到末个 }
        var i = s.IndexOf('{'); var j = s.LastIndexOf('}');
        if (i >= 0 && j > i) s = s[i..(j + 1)];
        return s.Trim();
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int GetInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return 0;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var v) ? v : (int)Math.Round(p.GetDouble()),
            JsonValueKind.String => int.TryParse(p.GetString(), out var v) ? v : 0,
            _ => 0,
        };
    }

    private static List<string> GetStringList(JsonElement e, string name)
    {
        var list = new List<string>();
        if (e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var it in arr.EnumerateArray())
                if (it.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(it.GetString()))
                    list.Add(it.GetString()!.Trim());
        return list;
    }
}

internal static class ReviewLogExt
{
    public static void LogDebugJson(this ILogger logger, string code, string raw)
        => logger.LogDebug("复评 {Code} 解析 JSON 失败，原文：{Raw}", code,
            raw.Length > 300 ? raw[..300] : raw);
}
