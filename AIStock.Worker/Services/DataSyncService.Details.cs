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
                var info = await FetchBaseInfoAsync(client, code, ct);
                var concepts = await FetchConceptsAsync(client, code, ct);
                return (code, info, concepts);
            });
            var results = await Task.WhenAll(tasks);

            foreach (var (code, info, concepts) in results)
            {
                if (info != null)
                {
                    var entity = await db.StockBase.FirstOrDefaultAsync(s => s.Code == code, ct);
                    if (entity != null)
                    {
                        if (!string.IsNullOrEmpty(info.Industry)) { entity.Industry = info.Industry; industryUpdated++; }
                        if (!string.IsNullOrEmpty(info.CompanyName)) entity.CompanyName = info.CompanyName;
                        if (!string.IsNullOrEmpty(info.SubIndustry)) entity.SubIndustry = info.SubIndustry;
                        if (!string.IsNullOrEmpty(info.MainBusiness)) entity.MainBusiness = info.MainBusiness;
                        if (!string.IsNullOrEmpty(info.Profile)) entity.Profile = info.Profile;
                        if (!string.IsNullOrEmpty(info.Province)) entity.Province = info.Province;
                        if (!string.IsNullOrEmpty(info.Website)) entity.Website = info.Website;
                        if (info.EmployeeCount.HasValue) entity.EmployeeCount = info.EmployeeCount;
                        if (info.RegCapital.HasValue) entity.RegCapital = info.RegCapital;
                        if (info.ListDate.HasValue) entity.ListDate = info.ListDate;
                        entity.UpdatedAt = DateTime.UtcNow;
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

    /// <summary>东财 F10 RPT_F10_BASIC_ORGINFO 取行业及公司基础信息</summary>
    private async Task<StockBaseInfoDto?> FetchBaseInfoAsync(HttpClient client, string code, CancellationToken ct)
    {
        try
        {
            var secucode = $"{code}.{GetMarketSuffix(code)}";
            var url = $"https://datacenter.eastmoney.com/securities/api/data/v1/get?reportName=RPT_F10_BASIC_ORGINFO&columns=ALL&quoteColumns=&filter=(SECUCODE%3D%22{secucode}%22)&pageNumber=1&pageSize=1&source=HSF10&client=PC";
            var json = await client.GetStringAsync(url, ct);
            var resp = JsonSerializer.Deserialize<EmF10Response>(json);
            var data = resp?.Result?.Data?.FirstOrDefault();
            if (data == null) return null;

            return new StockBaseInfoDto
            {
                Industry = ParseTop(data.IndustryCSRC1),           // 一级行业
                SubIndustry = ParseLast(data.BoardNameLevel),       // 细分（取最后一级）
                CompanyName = data.OrgName,
                MainBusiness = data.MainBusiness,
                Profile = data.OrgProfile,
                Province = data.Province,
                Website = data.OrgWeb,
                EmployeeCount = data.EmpNum,
                RegCapital = ParseDecimal(data.RegCapital),
                ListDate = ParseDate(data.ListingDate)
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "F10 基础信息获取失败 {Code}", code);
            return null;
        }
    }

    private static string? ParseTop(string? s)
        => string.IsNullOrEmpty(s) ? null : s.Split('-')[0].Trim();

    private static string? ParseLast(string? s)
        => string.IsNullOrEmpty(s) ? null : s.Split('-')[^1].Trim();

    private static DateTime? ParseDate(string? s)
        => DateTime.TryParse(s, out var d) ? d : null;

    private static decimal? ParseDecimal(object? v) => v switch
    {
        null => null,
        decimal d => d,
        int i => i,
        long l => l,
        double db => (decimal)db,
        string s when decimal.TryParse(s, out var p) => p,
        JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var jp) => jp,
        JsonElement je when je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var sp) => sp,
        _ => null
    };

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
        [JsonPropertyName("ORG_NAME")] public string? OrgName { get; set; }
        [JsonPropertyName("ORG_PROFILE")] public string? OrgProfile { get; set; }
        [JsonPropertyName("MAIN_BUSINESS")] public string? MainBusiness { get; set; }
        [JsonPropertyName("PROVINCE")] public string? Province { get; set; }
        [JsonPropertyName("ORG_WEB")] public string? OrgWeb { get; set; }
        [JsonPropertyName("EMP_NUM")] public int? EmpNum { get; set; }
        [JsonPropertyName("REG_CAPITAL")] public JsonElement? RegCapital { get; set; }
        [JsonPropertyName("LISTING_DATE")] public string? ListingDate { get; set; }
        [JsonPropertyName("BOARD_NAME_LEVEL")] public string? BoardNameLevel { get; set; }
    }

    private class StockBaseInfoDto
    {
        public string? Industry { get; set; }
        public string? SubIndustry { get; set; }
        public string? CompanyName { get; set; }
        public string? MainBusiness { get; set; }
        public string? Profile { get; set; }
        public string? Province { get; set; }
        public string? Website { get; set; }
        public int? EmployeeCount { get; set; }
        public decimal? RegCapital { get; set; }
        public DateTime? ListDate { get; set; }
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
