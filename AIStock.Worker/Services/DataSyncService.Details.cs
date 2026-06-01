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
        // 先刷新东财板块目录（概念/行业/地域），供概念名校验/导航
        await SyncBoardCatalogAsync(ct);

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

        var client = _httpClientFactory.CreateClient("eastmoney"); // 东财F10走隧道代理（同花顺顺带，隧道代理通用）
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

    /// <summary>同步单只股票的概念关联（新概念插入，已有的刷新排名/理由/板块代码）</summary>
    private static async Task UpsertConceptsAsync(AIStockDbContext db, string code, List<ConceptDto> concepts, CancellationToken ct)
    {
        var existing = await db.StockConceptRelation
            .Where(r => r.StockCode == code)
            .ToListAsync(ct);
        var byName = existing.ToDictionary(r => r.ConceptName);

        foreach (var c in concepts)
        {
            if (string.IsNullOrEmpty(c.Name)) continue;
            if (byName.TryGetValue(c.Name, out var row))
            {
                if (!string.IsNullOrEmpty(c.BoardCode)) row.QuoteCode = c.BoardCode;
                row.BoardRank = c.Rank;
                row.SelectedReason = c.Reason;
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var ent = new StockConceptRelationEntity
                {
                    StockCode = code,
                    ConceptName = c.Name,
                    QuoteCode = c.BoardCode,
                    BoardRank = c.Rank,
                    SelectedReason = c.Reason,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.StockConceptRelation.Add(ent);
                byName[c.Name] = ent; // 同批去重
            }
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

    /// <summary>东财个股核心题材（RPT_F10_CORETHEME_BOARDTYPE，IS_PRECISE=1 精确匹配，按 BOARD_RANK 升序）</summary>
    private async Task<List<ConceptDto>> FetchConceptsAsync(HttpClient client, string code, CancellationToken ct)
    {
        try
        {
            var secucode = $"{code}.{GetMarketSuffix(code)}";
            var url = "https://datacenter.eastmoney.com/securities/api/data/v1/get?" +
                      "reportName=RPT_F10_CORETHEME_BOARDTYPE&" +
                      "columns=SECUCODE,SECURITY_CODE,NEW_BOARD_CODE,BOARD_NAME,SELECTED_BOARD_REASON,BOARD_RANK&" +
                      $"filter=(SECUCODE=%22{secucode}%22)(IS_PRECISE=%221%22)&" +
                      "pageNumber=1&pageSize=50&sortColumns=BOARD_RANK&sortTypes=1&source=HSF10&client=PC";
            var json = await client.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(json);
            var list = new List<ConceptDto>();
            if (doc.RootElement.TryGetProperty("result", out var res) &&
                res.ValueKind == JsonValueKind.Object &&
                res.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var name = item.TryGetProperty("BOARD_NAME", out var bn) ? bn.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    list.Add(new ConceptDto
                    {
                        Name = name,
                        BoardCode = item.TryGetProperty("NEW_BOARD_CODE", out var bc) ? bc.GetString() : null,
                        Reason = item.TryGetProperty("SELECTED_BOARD_REASON", out var rs) ? rs.GetString() : null,
                        Rank = item.TryGetProperty("BOARD_RANK", out var rk) && rk.ValueKind == JsonValueKind.Number ? rk.GetInt32() : null,
                    });
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "东财核心题材获取失败 {Code}", code);
            return new List<ConceptDto>();
        }
    }

    // 东财板块类型（akshare 通行映射）：t:1 地域 / t:2 行业 / t:3 概念。本机联调请核对各类数量。
    private static readonly (int T, string Type)[] BoardTypes = { (1, "地域"), (2, "行业"), (3, "概念") };

    /// <summary>同步东财板块目录（概念/行业/地域全量）到 concept_board。</summary>
    public async Task<int> SyncBoardCatalogAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("eastmoney"); // 走隧道代理
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIStockDbContext>();

        var existing = await db.ConceptBoard.ToDictionaryAsync(b => b.BoardCode, ct);
        var now = DateTime.UtcNow;
        var total = 0;

        foreach (var (t, type) in BoardTypes)
        {
            try
            {
                var url = "https://push2.eastmoney.com/api/qt/clist/get?" +
                          $"pn=1&pz=600&po=1&np=1&fltt=2&invt=2&fid=f12&fs=m:90+t:{t}&fields=f12,f14&" +
                          "ut=8dec03ba335b81bf4ebdf7b29ec27d15";
                var json = await client.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("diff", out var diff)) continue;

                var items = diff.ValueKind == JsonValueKind.Array
                    ? diff.EnumerateArray()
                    : diff.EnumerateObject().Select(p => p.Value);

                var cnt = 0;
                foreach (var it in items)
                {
                    var bcode = it.TryGetProperty("f12", out var c) ? c.GetString() : null;
                    var bname = it.TryGetProperty("f14", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(bcode) || string.IsNullOrEmpty(bname)) continue;

                    if (existing.TryGetValue(bcode, out var row))
                    {
                        row.BoardName = bname; row.BoardType = type; row.UpdatedAt = now;
                    }
                    else
                    {
                        var e = new ConceptBoardEntity { BoardCode = bcode, BoardName = bname, BoardType = type, UpdatedAt = now };
                        db.ConceptBoard.Add(e);
                        existing[bcode] = e;
                    }
                    cnt++;
                }
                total += cnt;
                _logger.LogInformation("板块目录[{Type}] t:{T}：{Count} 个", type, t, cnt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "板块目录采集失败 t:{T}", t);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("板块目录同步完成：累计 {Total} 个", total);
        return total;
    }

    private static string GetMarketSuffix(string code)
    {
        if (code.StartsWith("6")) return "SH";
        if (code.StartsWith("0") || code.StartsWith("3")) return "SZ";
        if (code.StartsWith("8") || code.StartsWith("9") || code.StartsWith("4")) return "BJ";
        return "SZ";
    }

    private class ConceptDto
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>东财板块代码 NEW_BOARD_CODE</summary>
        public string? BoardCode { get; set; }
        /// <summary>题材排名 BOARD_RANK</summary>
        public int? Rank { get; set; }
        /// <summary>入选理由 SELECTED_BOARD_REASON</summary>
        public string? Reason { get; set; }
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

}
