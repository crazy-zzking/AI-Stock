# AI-Stock 改进建议清单

> 基于《AI-Stock 多方案架构改造总结（最终版）》建议，逐项对比当前 C#/.NET 代码库实际状态，生成改进清单。

---

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-05-28 | 初版生成 |
| 2026-05-28 | 复查更新：记录本轮修复项 |

### 本轮已修复项

| 修复项 | 说明 | 涉及文件 |
|--------|------|---------|
| Agent DI 自动收集 | AgentOrchestrator 构造函数注入 `IEnumerable<IAgent>`，启动时自动注册所有 Agent，消除手动 scope 注册 | [AgentOrchestrator.cs](file:///d:/source/repos/AI-Stock/AIStock.Orchestrator/AgentOrchestrator.cs#L14) / [DependencyInjection.cs](file:///d:/source/repos/AI-Stock/AIStock.Orchestrator/DependencyInjection.cs) |
| HttpClient 韧性策略 | `AddStandardResilienceHandler` 引入 Polly 重试（3次/指数退避）+ 断路器 + 超时 | [Program.cs](file:///d:/source/repos/AI-Stock/AIStock.Web/Program.cs#L87-L97) |
| EF Core 版本兼容 | `UseMySQL` → `UseMySql` + `ServerVersion.AutoDetect` | [Program.cs](file:///d:/source/repos/AI-Stock/AIStock.Web/Program.cs#L46) |
| Program.cs 精简 | 移除手动 Agent scope 注册块（~10行），改由 DI 自动处理 | [Program.cs](file:///d:/source/repos/AI-Stock/AIStock.Web/Program.cs) |

> ⚠️ 注意：本轮修复均为工程化改进，**P0/P1 核心改进项（Prompt Registry / Agent Memory / Parallel DAG / Observability 等）尚未开始**。

---

## 一、项目实际状态速览

| 维度 | 文档评分 | 实际状态 | 说明 |
|------|---------|---------|------|
| Unified Data Layer | ❌ 缺失 | ✅ 已完成 | IDataProvider + IDataProviderResolver，已接入 4 个数据源 |
| Backtest Engine | ❌ 缺失 | ✅ 已完成 | 含滑点/手续费/印花税/涨跌停/Sharpe/MDD |
| Risk Engine | ❌ 缺失 | ✅ 已完成 | 单笔/总仓位/单票/板块集中度 + 四级风险评级 |
| Portfolio Engine | ❌ 缺失 | ✅ 已完成 | 目标仓位/再平衡/权重计算 |
| Alpha/因子系统 | ❌ 缺失 | ✅ 已完成 | IAlphaEngine + AlphaEngineService |
| Knowledge Graph | ❌ 缺失 | ✅ 已完成 | 公司关系图谱 + 产业链图谱 + 概念扩散推演 |
| Feature Store | ❌ 缺失 | ✅ 已完成 | IFeatureStore + FeatureStoreService |
| Event Engine | ❌ 缺失 | ✅ 已完成 | 事件抽取→情绪→强度→持久化→消息总线 |
| Message Bus | ❌ 缺失 | ✅ 已完成 | RedisMessageBus（Redis Streams） |
| OMS/订单管理 | ❌ 缺失 | ✅ 已完成 | IOrderManager + OrderManagerService |
| Stop Loss | ❌ 缺失 | ✅ 已完成 | ATR止损/固定止损/移动止盈 |
| Market State | ❌ 缺失 | ✅ 已完成 | IMarketStateDetector（情绪/极端行情） |
| Multi-Agent | ❌ 缺失 | ✅ 已完成 | 3个Agent，通过 DI `IEnumerable<IAgent>` 自动收集，已消除手动注册 |
| Prompt Registry | ❌ 缺失 | ❌ 缺失 | Prompt 以字符串嵌入代码，无版本管理 |
| Agent Memory | ❌ 缺失 | ❌ 缺失 | Agent 完全无状态，无跨会话记忆 |
| Parallel Runtime | ❌ 缺失 | ⚠️ 部分 | LLM 多模型并行已实现，Agent 编排仍串行 |
| Observability | ❌ 缺失 | ❌ 缺失 | 仅 Serilog 日志，无 Tracing/Metrics |
| GraphRAG | ❌ 缺失 | ❌ 缺失 | 知识图谱未融入 LLM 推理 |
| Agent Reflection | ❌ 缺失 | ❌ 缺失 | 无自我反思/纠错能力 |
| Auto Trading 闭环 | ❌ 缺失 | ⚠️ 部分 | 分析→信号→风控有，缺自动下单环节 |
| SaaS 化 | ❌ 缺失 | ❌ 未做 | 无多租户/RBAC/计费 |
| 策略插件系统 | ❌ 缺失 | ❌ 未做 | 策略硬编码，无法热插拔 |

---

## 二、P0 — 必须优先改进

### 1. Prompt Registry（Prompt 注册中心）

**当前问题：**

Prompt 以 `LLMRequest.SystemPrompt` / `UserPrompt` 字符串形式直接在调用处拼接，无版本管理、无模板引擎、无法 A/B 测试、无法热更新。

**建议方案：**

```
prompts/
├── technical/
│   ├── trend-analysis.v1.yaml
│   ├── trend-analysis.v2.yaml
│   └── breakout-detection.v1.yaml
├── macro/
│   ├── market-overview.v1.yaml
│   └── policy-impact.v1.yaml
├── sentiment/
│   ├── news-sentiment.v1.yaml
│   └── social-sentiment.v1.yaml
├── strategy/
│   ├── signal-generation.v1.yaml
│   └── decision-summary.v1.yaml
└── risk/
    ├── risk-assessment.v1.yaml
    └── black-swan-check.v1.yaml
```

**Prompt 配置格式（YAML）：**

```yaml
name: trend-analysis
version: v2
model: deepseek-v3
temperature: 0.3
max_tokens: 2000
variables:
  - code
  - kline_data
  - indicators
output_schema:
  trend: enum[up,down,sideways]
  confidence: float
  reasoning: string
```

**涉及模块：**
- 新建 `AIStock.Prompt` 项目
- 修改 `AIStock.LLM` — 通过 PromptRegistry 获取 Prompt 模板
- 修改 `AIStock.Orchestrator.Agents` — 不再硬编码 Prompt 字符串

---

### 2. Agent Memory（Agent 长期记忆）

**当前问题：**

查看 [ResearchAgent](file:///d:/source/repos/AI-Stock/AIStock.Orchestrator/Agents/ResearchAgent.cs)，每次 `ExecuteAsync` 都是独立执行，不记录也不回溯历史分析。`GetStatusAsync` 返回的是硬编码的空状态。Agent 无法形成长期研究能力。

**建议方案：**

```csharp
public interface IAgentMemory
{
    /// 保存分析结果
    Task SaveAnalysisAsync(string agentId, string code, AnalysisRecord record);
    
    /// 获取历史分析记录
    Task<List<AnalysisRecord>> GetHistoryAsync(string agentId, string code, int count = 10);
    
    /// 语义搜索相似历史场景
    Task<List<AnalysisRecord>> SearchSimilarAsync(string agentId, string query, int count = 5);
    
    /// 获取 Agent 长期跟踪的标的变化
    Task<WatchlistSnapshot> GetWatchlistSnapshotAsync(string agentId);
}
```

**技术选型：**
- 短期记忆：MySQL `AgentMemory` 表
- 语义搜索：Qdrant / Milvus 向量数据库
- 轻量方案：先用 EF Core + JSON 列存储，后续再引入向量库

**涉及模块：**
- 新建 `AIStock.Memory` 项目
- 修改所有 Agent — 注入 `IAgentMemory`，执行前后读写记忆

---

### 3. Parallel DAG 执行（Agent 并行编排）

**本轮修复：** AgentOrchestrator 已改为通过 `IEnumerable<IAgent>` 自动收集 Agent（DI 改进），但核心问题未变。

**当前问题：**

[AgentOrchestrator.ExecuteWorkflowAsync](file:///d:/source/repos/AI-Stock/AIStock.Orchestrator/AgentOrchestrator.cs#L38) 使用 `foreach` 串行执行步骤。虽然有 `DependsOn` DAG 定义，但无依赖关系的步骤并不会并行执行。

**当前代码（串行 — 未变）：**

```csharp
foreach (var step in workflow.Steps)
{
    if (!step.DependsOn.All(d => completedSteps.Contains(d)))
    {
        continue;  // 依赖未满足则跳过（但先遇到后遇到的问题依然存在）
    }
    var agentResult = await agent.ExecuteAsync(task);
    stepResults[step.StepId] = agentResult;
    completedSteps.Add(step.StepId);
}
```

**建议改为（拓扑排序 + 并行）：**

```csharp
// 按拓扑层级分组
var levels = TopologicalSort(workflow.Steps);
foreach (var level in levels)
{
    // 同层级并行执行
    var tasks = level.Select(step => ExecuteStepAsync(step, stepResults));
    await Task.WhenAll(tasks);
}
```

**涉及模块：**
- 修改 `AIStock.Orchestrator/AgentOrchestrator.cs`

---

## 三、P1 — 中期改进

### 4. Auto Trading 闭环（自动下单）

**当前问题：**

[AutonomousDecisionSystem](file:///d:/source/repos/AI-Stock/AIStock.Orchestrator/AutonomousDecisionSystem.cs) 实现了"分析→信号→风控"链路，但到 `RiskCheck` 就结束了，缺少最关键的一步：**风控通过后自动调用 IOrderManager 下单**。

**建议补充：**

```csharp
// 在 MakeDecisionAsync 中，风控通过后
if (riskResult.Success && riskResult.RiskCheck.Passed)
{
    var orderRequest = new OrderRequest
    {
        Code = request.Code,
        Side = signal.SignalType,  // "buy" or "sell"
        OrderType = OrderType.Limit,
        Price = signal.Price,
        Volume = signal.Volume,
        StrategyName = "AutonomousDecision",
        SignalId = signal.SignalId
    };
    result.Order = await _orderManager.PlaceOrderAsync(orderRequest);
}
```

**涉及模块：**
- 修改 `AIStock.Orchestrator/AutonomousDecisionSystem.cs`
- 注入 `IOrderManager`

---

### 5. Observability（可观测性）

**当前问题：**

仅有 Serilog 日志，缺少：
- 分布式追踪（无法追踪一个请求跨多个 Agent 的调用链）
- 指标监控（LLM 调用延迟、Token 消耗、Agent 执行时间）
- AI 调用追踪（Prompt 效果、模型输出质量）

**建议方案：**

```csharp
// OpenTelemetry 集成
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());
```

**关键指标：**

| 指标 | 说明 |
|------|------|
| `llm_request_duration_ms` | LLM 调用延迟（按模型分） |
| `llm_token_usage_total` | Token 消耗总量 |
| `agent_execution_duration_ms` | Agent 执行耗时 |
| `agent_task_total` | Agent 任务计数（成功/失败） |
| `backtest_completed_total` | 回测完成计数 |
| `signal_generated_total` | 信号生成计数 |
| `order_placed_total` | 下单计数 |

**涉及模块：**
- 新建 NuGet 包引用：`OpenTelemetry.Extensions.Hosting`
- 修改 `AIStock.Web/Program.cs`
- 在 LLM/Agent/Strategy 关键路径添加 ActivitySource

---

### 6. GraphRAG（知识图谱增强 LLM）

**当前问题：**

[ICompanyRelationGraph](file:///d:/source/repos/AI-Stock/AIStock.Core/Interfaces/IKnowledgeGraph.cs) 和 [IIndustryChainGraph](file:///d:/source/repos/AI-Stock/AIStock.Core/Interfaces/IKnowledgeGraph.cs) 已实现，但知识图谱数据没有融入 LLM 推理流程——分析某只股票时，LLM 不知道它的供应商、客户、产业链位置。

**建议方案：**

```csharp
public interface IGraphRAGService
{
    /// 为 LLM 请求注入图谱上下文
    Task<GraphContext> BuildContextAsync(string code, string queryType);
}

public class GraphContext
{
    public string CompanyInfo { get; set; }        // 公司基本信息
    public string Suppliers { get; set; }          // 供应商关系
    public string Customers { get; set; }          // 客户关系
    public string IndustryChain { get; set; }      // 产业链位置
    public string RelatedEvents { get; set; }      // 相关事件
}
```

**典型场景：**

分析宁德时代时，自动注入：
```
产业链位置：动力电池 → 上游（锂/钴/镍）、下游（新能源车企）
主要客户：特斯拉、蔚来、理想
主要供应商：天齐锂业、华友钴业
近期事件：锂价下跌 → 利好成本端
```

**涉及模块：**
- 新建 `AIStock.GraphRAG` 项目或在 `AIStock.Knowledge` 中扩展
- 修改 Agent 调用 LLM 前注入图谱上下文

---

### 7. Agent Reflection（Agent 自我反思）

**当前问题：**

Agent 执行完任务后直接返回结果，没有自我检查、没有多轮思考、不会纠错。

**建议方案：**

在 Agent 执行流程中增加 Reflection 步骤：

```csharp
public async Task<AgentResult> ExecuteWithReflectionAsync(AgentTask task)
{
    // 1. 初始分析
    var initialResult = await ExecuteAsync(task);
    
    // 2. 自我反思
    var reflectionPrompt = BuildReflectionPrompt(initialResult);
    var reflection = await _llmService.SendAsync(new LLMRequest
    {
        SystemPrompt = "你是严谨的分析师，请检查以下分析是否有逻辑矛盾或遗漏。",
        UserPrompt = reflectionPrompt
    });
    
    // 3. 如有问题，修正后重新输出
    if (reflection.Content.Contains("矛盾") || reflection.Content.Contains("遗漏"))
    {
        return await ExecuteWithCorrectionAsync(task, reflection.Content);
    }
    
    return initialResult;
}
```

**涉及模块：**
- 修改 `AIStock.Orchestrator.Agents` 基类或创建 `ReflectiveAgent` 装饰器

---

## 四、P2 — 长期改进

### 8. 前端升级 — AI 投研工作台

**当前状态：**

基础 React 页面（Dashboard / Agents / Positions / Strategy），信息密度偏管理后台风格。

**建议方向：**

| 功能 | 推荐方案 |
|------|---------|
| K 线图表 | TradingView Widget / lightweight-charts |
| 资金流 | ECharts Sankey 图 |
| 产业链 | D3.js / ECharts 关系图 |
| AI 对话 | Chat 面板（类似 ChatGPT 界面） |
| 多 Agent 面板 | 各 Agent 分析结果并列展示 |
| 因子热力图 | ECharts Heatmap |

---

### 9. SaaS 化

**需要增加的模块：**

- 用户体系（注册/登录/JWT）
- RBAC 权限（管理员/分析师/只读）
- API Key 管理
- 多租户数据隔离
- 使用量计费

---

### 10. 策略插件系统

**当前状态：**

6 个策略（网格/均线突破/配对交易等）是硬编码的 Service 类。

**建议方向：**

- 策略动态加载（AssemblyLoadContext）
- 策略 YAML/JSON 配置化
- 策略市场（上传/分享/下载）
- Workflow 可视化编排（拖拽式 DAG）

---

## 五、改进优先级总览

```
P0（立即做）
├── Prompt Registry      ← Prompt 失控风险
├── Agent Memory          ← Agent 无长期能力
└── Parallel DAG 执行      ← Agent 增多后串行瓶颈

P1（近期做）
├── Auto Trading 闭环     ← 决策链最后一环缺失
├── Observability         ← 排障靠日志，无追踪
├── GraphRAG              ← 知识图谱未融入 LLM
└── Agent Reflection      ← 无自我纠错

P2（远期做）
├── 前端升级              ← 金融终端体验
├── SaaS 化              ← 商业化基础
└── 策略插件系统           ← 生态扩展
```

---

## 六、涉及文件清单

| 改进项 | 涉及文件 |
|--------|---------|
| Prompt Registry | 新建 `AIStock.Prompt/`，修改 `AIStock.LLM/Services/LLMService.cs`，修改 3 个 Agent |
| Agent Memory | 新建 `AIStock.Memory/`，修改 `AIStock.Core/Interfaces/IAgent.cs`，修改 3 个 Agent |
| Parallel DAG | 修改 `AIStock.Orchestrator/AgentOrchestrator.cs` |
| Auto Trading | 修改 `AIStock.Orchestrator/AutonomousDecisionSystem.cs` |
| Observability | 修改 `AIStock.Web/Program.cs`，各模块添加 ActivitySource |
| GraphRAG | 新建 `AIStock.GraphRAG/`，修改 Agent 调用 LLM 逻辑 |
| Agent Reflection | 修改 Agent 基类或新建装饰器 |

---

> **生成时间**：2026-05-28  
> **基于文档**：《AI-Stock 多方案架构改造总结（最终版）.md》  
> **对比基准**：当前 C#/.NET 代码库 `d:\source\repos\AI-Stock`
