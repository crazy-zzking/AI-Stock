using System.Text;
using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Infrastructure.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Worker.Services;

/// <summary>
/// 概念炒作点蒸馏：把 stock_concept_relation.selected_reason（公告体长文）用 LLM 提炼成 ≤12 字短语，
/// 写入 concept_digest，供选股展示。幂等可断点续跑（只蒸馏 digest 为空的行）；批量合一次调用 + 并发限流。
/// </summary>
public class ConceptDigestService
{
    private const int MaxReasonChars = 220;   // 截断理由控 token
    private const int MaxDigestChars = 60;    // 与列长一致

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConceptDigestService> _logger;

    public ConceptDigestService(IServiceScopeFactory scopeFactory, ILogger<ConceptDigestService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>蒸馏待处理关联。batchSize 每批条数(合一次LLM)，maxConcurrency 并发批数，maxRows>0 限量(调试)。返回成功条数。</summary>
    public async Task<int> SyncAsync(int batchSize = 1, int maxConcurrency = 5, int maxRows = 0, CancellationToken ct = default)
    {
        List<long> pendingIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
            var q = db.StockConceptRelation
                .Where(r => r.SelectedReason != null && r.SelectedReason != "" && r.ConceptDigest == null)
                .OrderBy(r => r.Id).Select(r => r.Id);
            pendingIds = await (maxRows > 0 ? q.Take(maxRows) : q).ToListAsync(ct);
        }
        if (pendingIds.Count == 0) { _logger.LogInformation("概念蒸馏：无待处理关联"); return 0; }
        _logger.LogInformation("概念蒸馏：待处理 {N} 条，批大小 {B} 并发 {C}", pendingIds.Count, batchSize, maxConcurrency);

        var batches = new List<List<long>>();
        for (int i = 0; i < pendingIds.Count; i += batchSize) batches.Add(pendingIds.Skip(i).Take(batchSize).ToList());

        var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var done = 0;
        var processed = 0;
        await Task.WhenAll(batches.Select(async batch =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var n = await ProcessBatchAsync(batch, ct);
                Interlocked.Add(ref done, n);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "概念蒸馏批次失败，跳过 {N} 条", batch.Count); }
            finally
            {
                sem.Release();
                var p = Interlocked.Add(ref processed, batch.Count);
                if (p % 500 < batchSize) _logger.LogInformation("概念蒸馏进度 {P}/{T}", p, pendingIds.Count);
            }
        }));
        _logger.LogInformation("概念蒸馏完成：成功 {Done}/{Total}", done, pendingIds.Count);
        return done;
    }

    private async Task<int> ProcessBatchAsync(List<long> ids, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();
        var llm = scope.ServiceProvider.GetRequiredService<ILLMService>();

        var rows = await db.StockConceptRelation.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        rows = rows.Where(r => !string.IsNullOrWhiteSpace(r.SelectedReason)).ToList();
        if (rows.Count == 0) return 0;

        var digests = await DistillAsync(llm, rows, ct);
        if (digests == null || digests.Count != rows.Count)
        {
            // 批量对齐失败 → 逐条降级，保证不整批丢
            digests = new List<string>();
            foreach (var r in rows)
            {
                var one = await DistillAsync(llm, new[] { r }, ct);
                digests.Add(one != null && one.Count == 1 ? one[0] : "");
            }
        }

        var n = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var d = Clean(digests[i]);
            if (string.IsNullOrEmpty(d)) continue;
            rows[i].ConceptDigest = d;
            rows[i].UpdatedAt = DateTime.Now;
            n++;
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>调一次 LLM 把 rows 蒸馏成短语数组（按序对齐）。失败/解析不出返回 null。</summary>
    private async Task<List<string>?> DistillAsync(ILLMService llm, IReadOnlyList<Infrastructure.Database.Entities.StockConceptRelationEntity> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var reason = rows[i].SelectedReason!.Trim();
            if (reason.Length > MaxReasonChars) reason = reason.Substring(0, MaxReasonChars);
            sb.Append(i + 1).Append(". 概念=").Append(rows[i].ConceptName).Append(" | 理由=").Append(reason).Append('\n');
        }

        var req = new LLMRequest
        {
            SystemPrompt = "你是A股题材分析助手。把每条「概念+入选理由」提炼成≤12字的炒作点短语，"
                + "只保留最核心的产品/技术/催化（如“类ABF膜国产替代”“控股算力子公司”），去掉公告套话与持股比例细节。"
                + "严格输出 JSON 字符串数组，元素顺序与输入序号一一对应，长度必须等于输入条数，只输出 JSON、不要解释、不要代码块标记。",
            UserPrompt = sb.ToString(),
            Temperature = 0.2m,
            MaxTokens = Math.Min(2000, 80 + rows.Count * 30),
        };

        try
        {
            var resp = await llm.SendAsync(req, ct);
            return ParseArray(resp?.Content);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "概念蒸馏 LLM 调用失败"); return null; }
    }

    /// <summary>从 LLM 文本里提取 JSON 字符串数组（容忍 ```json 包裹/前后缀文字）。</summary>
    private static List<string>? ParseArray(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start) return null;
        var json = content.Substring(start, end - start + 1);
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json);
            return arr;
        }
        catch { return null; }
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim().Trim('"', '「', '」', '【', '】', ' ', '。');
        if (s.Length > MaxDigestChars) s = s.Substring(0, MaxDigestChars);
        return s;
    }
}
