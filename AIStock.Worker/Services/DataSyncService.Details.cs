using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Worker.Services;

/// <summary>
/// 股票明细同步（行业 + 概念）— 行业来自东财 F10，概念来自同花顺。
/// </summary>
public partial class DataSyncService
{
    /// <summary>
    /// 同步股票明细：东财 F10 行业 → stock_base.Industry；同花顺概念 → stock_concept_relation。
    /// </summary>
    public async Task<int> SyncStockDetailsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var codesQuery = db.StockBase.Where(s => !s.IsDelisted).OrderBy(s => s.Code).Select(s => s.Code);
        if (_options.DetailMaxStocks > 0)
            codesQuery = codesQuery.Take(_options.DetailMaxStocks);
        var codes = await codesQuery.ToListAsync(ct);

        if (codes.Count == 0)
        {
            _logger.LogWarning("stock_base 无股票，跳过明细同步");
            return 0;
        }

        var client = _httpClientFactory.CreateClient("default");
        var batchSize = Math.Max(1, _options.DetailBatchSize);
        var processed = 0;
        var industryUpdated = 0;

        _logger.LogInformation("开始股票明细同步：{Count} 只股票", codes.Count);

        for (int i = 0; i < codes.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;
            var batch = codes.Skip(i).Take(batchSize).ToList();

            var tasks = batch.Select(async code =>
            {
                var industry = await FetchIndustryAsync(client, code, ct);
                var concepts = await FetchConceptsAsync(client, code, ct);
                return (code, industry, concepts);
            });
            var results = await Task.WhenAll(tasks);

            foreach (var (code, industry, concepts) in results)
            {
                if (!string.IsNullOrEmpty(industry))
                {
                    var entity = await db.StockBase.FirstOrDefaultAsync(s => s.Code == code, ct);
                    if (entity != null)
                    {
                        entity.Industry = industry;
                        entity.UpdatedAt = DateTime.UtcNow;
                        industryUpdated++;
                    }
                }

                if (concepts.Count > 0)
                    await UpsertConceptsAsync(db, code, concepts, ct);
            }

            await db.SaveChangesAsync(ct);
            processed += batch.Count;

            if (processed % 100 == 0 || i + batchSize >= codes.Count)
                _logger.LogInformation("明细同步进度：{Processed}/{Total}，行业更新 {Industry}", processed, codes.Count, industryUpdated);

            if (_options.DetailBatchDelayMs > 0 && i + batchSize < codes.Count)
                await Task.Delay(_options.DetailBatchDelayMs, ct);
        }

        _logger.LogInformation("股票明细同步完成：{Total} 只，行业更新 {Industry} 只", codes.Count, industryUpdated);
        return industryUpdated;
    }

    /// <summary>同步单只股票的概念关联（只增量补充未有的概念）</summary>
    private static async Task UpsertConceptsAsync(AIStockDbContext db, string code, List<ConceptDto> concepts, CancellationToken ct)
    {
        var existing = await db.StockConceptRelation
            .Where(r => r.StockCode == code)
            .Select(r => r.ConceptName)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        foreach (var c in concepts)
        {
            if (string.IsNullOrEmpty(c.Name) || existingSet.Contains(c.Name)) continue;
            db.StockConceptRelation.Add(new StockConceptRelationEntity
            {
                StockCode = code,
                ConceptName = c.Name,
                QuoteCode = c.QuoteCode,
                ConceptId = c.ConceptId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            existingSet.Add(c.Name);
        }
    }

    /// <summary>东财 F10 RPT_F10_BASIC_ORGINFO 取一级行业</summary>
    private async Task<string?> FetchIndustryAsync(HttpClient client, string code, CancellationToken ct)
    {
        try
        {
            var secucode = $"{code}.{GetMarketSuffix(code)}";
            var url = $"https://datacenter.eastmoney.com/securities/api/data/v1/get?reportName=RPT_F10_BASIC_ORGINFO&columns=ALL&quoteColumns=&filter=(SECUCODE%3D%22{secucode}%22)&pageNumber=1&pageSize=1&source=HSF10&client=PC";
            var json = await client.GetStringAsync(url, ct);
            var resp = JsonSerializer.Deserialize<EmF10Response>(json);
            var industry = resp?.Result?.Data?.FirstOrDefault()?.IndustryCSRC1;
            if (string.IsNullOrEmpty(industry)) return null;
            // 格式如"制造业-专用设备制造业"，取一级
            var parts = industry.Split('-');
            return parts.Length > 0 ? parts[0].Trim() : industry.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "F10 行业获取失败 {Code}", code);
            return null;
        }
    }

    /// <summary>同花顺概念列表</summary>
    private async Task<List<ConceptDto>> FetchConceptsAsync(HttpClient client, string code, CancellationToken ct)
    {
        try
        {
            var marketId = GetMarketId(code);
            var url = $"https://basic.10jqka.com.cn/fuyao/f10_stock_index/concept/v1/stock_concept_list?simple=1&market_id={marketId}&code={code}";
            var json = await client.GetStringAsync(url, ct);
            var resp = JsonSerializer.Deserialize<ThsConceptResponse>(json);
            if (resp?.StatusCode != 0 || resp.Data == null) return new List<ConceptDto>();
            return resp.Data
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => new ConceptDto { Name = c.Name!, QuoteCode = c.QuoteCode, ConceptId = c.ConceptId })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "同花顺概念获取失败 {Code}", code);
            return new List<ConceptDto>();
        }
    }

    private static string GetMarketSuffix(string code)
    {
        if (code.StartsWith("6")) return "SH";
        if (code.StartsWith("0") || code.StartsWith("3")) return "SZ";
        if (code.StartsWith("8") || code.StartsWith("9") || code.StartsWith("4")) return "BJ";
        return "SZ";
    }

    private static int GetMarketId(string code)
    {
        if (code.StartsWith("6")) return 17;
        if (code.StartsWith("0") || code.StartsWith("3")) return 33;
        if (code.StartsWith("8") || code.StartsWith("9") || code.StartsWith("4")) return 151;
        return 33;
    }

    private class ConceptDto
    {
        public string Name { get; set; } = string.Empty;
        public string? QuoteCode { get; set; }
        public int? ConceptId { get; set; }
    }

    private class EmF10Response
    {
        [JsonPropertyName("result")] public EmF10Result? Result { get; set; }
    }

    private class EmF10Result
    {
        [JsonPropertyName("data")] public List<EmF10Org>? Data { get; set; }
    }

    private class EmF10Org
    {
        [JsonPropertyName("INDUSTRYCSRC1")] public string? IndustryCSRC1 { get; set; }
    }

    private class ThsConceptResponse
    {
        [JsonPropertyName("status_code")] public int StatusCode { get; set; }
        [JsonPropertyName("data")] public List<ThsConceptItem>? Data { get; set; }
    }

    private class ThsConceptItem
    {
        [JsonPropertyName("concept_id")] public int? ConceptId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("quote_code")] public string? QuoteCode { get; set; }
    }
}
