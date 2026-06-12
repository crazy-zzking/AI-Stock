using System.Text.Json;
using AIStock.Core.Interfaces;
using AIStock.Infrastructure.Database.Context;
using AIStock.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStock.Web.Controllers;

/// <summary>
/// Worker 任务配置（前端可配置）。读写 worker_config 表，Worker 端按 TTL 热读，改动无需重启即生效。
/// 配置分两层：调度层（Jobs：14 个任务的启停/周期/定点）与业务参数层（各采集任务的拉取条数/批大小/阈值等）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WorkerConfigController : ControllerBase
{
    /// <summary>Jobs 段名（与 Worker JobSchedulerOptions.SectionName 对应）。</summary>
    private const string JobsSection = "Jobs";

    /// <summary>业务参数段白名单（与 Worker 各 Options.SectionName 对应）。</summary>
    private static readonly Dictionary<string, string> BusinessSections = new()
    {
        ["DataSync"] = "数据同步",
        ["IntelligenceSync"] = "情报采集",
        ["MarketSnapshot"] = "市场快照/龙虎榜",
        ["KnowledgeStar"] = "知识星球",
        ["GraphPromotion"] = "候选边晋升",
    };

    /// <summary>15 个调度任务的展示元信息（顺序即前端展示顺序）。</summary>
    private static readonly (string Name, string Display, bool Dynamic, string Hint)[] JobMeta =
    {
        ("stock-base",      "股票池同步",            false, "建议每日定点"),
        ("stock-detail",    "股票明细(行业/概念)同步", false, "建议每日定点"),
        ("kline",           "日K线同步",             false, "收盘后/启动补拉"),
        ("news",            "财经新闻采集",          false, "周期间隔"),
        ("announcement",    "上市公司公告采集",       false, "周期间隔"),
        ("report",          "研报采集",              false, "周期间隔"),
        ("knowledge-star",  "知识星球采集",          true,  "动态间隔(盘中密集/盘后稀疏)"),
        ("graph-promote",   "候选边晋升",            false, "建议每日定点"),
        ("position-cache",  "持仓缓存刷新",          false, "周期间隔"),
        ("market-snapshot", "市场快照采集",          true,  "盘中动态/未启用盘中则每日定点"),
        ("dragon-tiger",    "龙虎榜采集",            false, "建议每日定点(收盘后)"),
        ("index-kline",     "指数日K同步",           false, "建议每日定点(收盘后)"),
        ("capital-flow",    "资金流历史同步",         false, "建议每日定点(收盘后)"),
        ("concept-digest",  "概念炒作点蒸馏",         false, "建议每日定点"),
        ("selection-performance", "选股绩效补算",     false, "建议每日定点(日K同步后)"),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Encoder = AIStock.Core.Json.AppJson.CjkEncoder,
    };

    private readonly IWorkerConfigProvider _config;
    private readonly AIStockDbContext _db;
    private readonly ILogger<WorkerConfigController> _logger;

    public WorkerConfigController(IWorkerConfigProvider config, AIStockDbContext db, ILogger<WorkerConfigController> logger)
    {
        _config = config;
        _db = db;
        _logger = logger;
    }

    /// <summary>取调度层配置：14 个任务的启停/周期/定点 + 展示元信息。</summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs(CancellationToken ct)
    {
        var json = await _config.GetRawAsync(JobsSection, ct);
        var dict = SafeDeserializeDict(json);

        var rows = JobMeta.Select(m =>
        {
            dict.TryGetValue(m.Name, out var o);
            o ??= new JobValueDto();
            return new
            {
                name = m.Name,
                displayName = m.Display,
                dynamic = m.Dynamic,
                hint = m.Hint,
                enabled = o.Enabled,
                runOnStartup = o.RunOnStartup,
                intervalSeconds = o.IntervalSeconds,
                dailyAtHour = o.DailyAtHour,
                dailyAtMinute = o.DailyAtMinute,
            };
        }).ToList();

        return Ok(rows);
    }

    /// <summary>保存调度层配置（整组覆盖 Jobs 段）。</summary>
    [HttpPut("jobs")]
    public async Task<IActionResult> SaveJobs([FromBody] List<JobConfigDto> jobs, CancellationToken ct)
    {
        if (jobs == null || jobs.Count == 0)
            return BadRequest(new { error = "jobs 不能为空" });

        var known = JobMeta.Select(m => m.Name).ToHashSet();
        var dict = new Dictionary<string, JobValueDto>();
        foreach (var j in jobs)
        {
            if (string.IsNullOrWhiteSpace(j.Name) || !known.Contains(j.Name))
                continue; // 忽略未知任务名，避免脏数据
            dict[j.Name] = new JobValueDto
            {
                Enabled = j.Enabled,
                RunOnStartup = j.RunOnStartup,
                IntervalSeconds = Math.Max(0, j.IntervalSeconds),
                DailyAtHour = j.DailyAtHour is >= 0 and <= 23 ? j.DailyAtHour : -1,
                DailyAtMinute = j.DailyAtMinute is >= 0 and <= 59 ? j.DailyAtMinute : 0,
            };
        }

        var json = JsonSerializer.Serialize(dict, JsonOpts);
        await _config.SaveAsync(JobsSection, json, ct);
        _logger.LogInformation("调度配置已更新：{Count} 个任务", dict.Count);
        return Ok(new { message = "已保存", count = dict.Count });
    }

    /// <summary>取各任务运行态（运行中/最近开始/耗时/上次结果）供前端轮询。</summary>
    [HttpGet("jobs/status")]
    public async Task<IActionResult> GetJobsStatus(CancellationToken ct)
    {
        var rows = await _db.WorkerJobRun.AsNoTracking().ToDictionaryAsync(r => r.JobName, ct);
        var list = JobMeta.Select(m =>
        {
            rows.TryGetValue(m.Name, out var r);
            return new
            {
                name = m.Name,
                isRunning = r?.IsRunning ?? false,
                lastStart = r?.LastStart,
                lastEnd = r?.LastEnd,
                lastDurationMs = r?.LastDurationMs,
                lastTrigger = r?.LastTrigger,
                lastSuccess = r?.LastSuccess,
                lastError = r?.LastError,
                runRequested = r?.RunRequested ?? false,
            };
        });
        return Ok(list);
    }

    /// <summary>请求立即运行某任务（置位 run_requested，由 Worker 轮询消费）。正在运行则不重复触发。</summary>
    [HttpPost("jobs/{name}/run")]
    public async Task<IActionResult> RunNow(string name, CancellationToken ct)
    {
        if (!JobMeta.Any(m => m.Name == name))
            return NotFound(new { error = $"未知任务: {name}" });

        var row = await _db.WorkerJobRun.FirstOrDefaultAsync(r => r.JobName == name, ct);
        if (row == null)
        {
            row = new WorkerJobRunEntity { JobName = name };
            _db.WorkerJobRun.Add(row);
        }
        if (row.IsRunning)
            return Ok(new { message = "任务正在运行中，未重复触发", running = true });

        row.RunRequested = true;
        row.RequestedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("已请求立即运行任务 {Name}", name);
        return Ok(new { message = "已触发，将在数秒内开始", running = false });
    }

    /// <summary>取某业务参数段的原始配置对象。</summary>
    [HttpGet("sections/{section}")]
    public async Task<IActionResult> GetSection(string section, CancellationToken ct)
    {
        if (!BusinessSections.ContainsKey(section))
            return NotFound(new { error = $"未知配置段: {section}" });

        var json = await _config.GetRawAsync(section, ct);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return Ok(doc.RootElement.Clone());
        }
        catch
        {
            return Ok(JsonDocument.Parse("{}").RootElement.Clone());
        }
    }

    /// <summary>保存某业务参数段（整段覆盖）。</summary>
    [HttpPut("sections/{section}")]
    public async Task<IActionResult> SaveSection(string section, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (!BusinessSections.ContainsKey(section))
            return NotFound(new { error = $"未知配置段: {section}" });
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "配置必须是 JSON 对象" });

        var json = JsonSerializer.Serialize(body, JsonOpts);
        await _config.SaveAsync(section, json, ct);
        _logger.LogInformation("业务配置段 {Section} 已更新", section);
        return Ok(new { message = "已保存" });
    }

    /// <summary>列出可配置的业务参数段（供前端构建 Tab）。</summary>
    [HttpGet("sections")]
    public IActionResult ListSections()
        => Ok(BusinessSections.Select(kv => new { section = kv.Key, displayName = kv.Value }));

    private static Dictionary<string, JobValueDto> SafeDeserializeDict(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JobValueDto>>(json, JsonOpts) ?? new();
        }
        catch
        {
            return new();
        }
    }
}

/// <summary>
/// 任务调度配置「值」（镜像 Worker 的 JobOptions；Web 不引用 Worker 项目，故独立定义）。存库 JSON 即此形状。
/// </summary>
public class JobValueDto
{
    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动后是否立即跑一次</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>周期间隔（秒），&gt;0 时按间隔运行</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>每日运行时刻(0-23)，&gt;=0 时优先于 IntervalSeconds；-1 表示不用</summary>
    public int DailyAtHour { get; set; } = -1;

    /// <summary>每日运行分钟(0-59)，仅当 DailyAtHour&gt;=0 时生效；默认 0</summary>
    public int DailyAtMinute { get; set; } = 0;
}

/// <summary>PUT /jobs 的入参项：调度值 + 任务名（name 仅作 key，不入存库值）。</summary>
public class JobConfigDto : JobValueDto
{
    /// <summary>任务名</summary>
    public string? Name { get; set; }
}
