# AI-Stock 修复任务清单

## 概述

基于全面代码审查，列出需要修复的问题和改造建议。排除安全凭据、测试覆盖、认证授权三项（已单独处理）。

**审查范围：** 15个后端项目 + 前端 + 脚本 + 文档

---

## 一、严重问题 (Critical)

### FIX-01: EF Core 无Migration

**问题：** 数据库Schema仅靠 `init.sql` 手动维护，`Program.cs` 使用 `EnsureCreated()` 创建数据库。无法演进Schema，不适合生产环境。

**影响：** 表结构变更需要手动执行SQL，无法版本化追踪，多人协作容易出现Schema不一致。

**修复方案：**
1. 在 `AIStock.Infrastructure` 中创建初始Migration
2. `Program.cs` 将 `EnsureCreated()` 替换为 `MigrateAsync()`
3. 建立Migration命名规范和提交流程

**涉及文件：**
- `AIStock.Infrastructure/AIStock.Infrastructure.csproj`
- `AIStock.Web/Program.cs`

**预估工时：** 2-3天

---

### FIX-02: Redis `KEYS` 命令用于生产

**问题：** `FeatureStoreService.GetHistoricalFeaturesAsync` 和 `CleanupExpiredFeaturesAsync` 使用 Redis `KEYS` 命令扫描全量Key，会阻塞Redis服务。

**影响：** 随着股票数量增长，Redis扫描时间线性增加，阻塞其他请求。

**修复方案：**
1. 将历史特征存储改为 Sorted Set，用时间戳作为Score
2. 使用 `ZRANGEBYSCORE` 替代 `KEYS` 模式匹配
3. `CleanupExpiredFeaturesAsync` 使用 `ZREMRANGEBYSCORE` 按时间范围删除

**涉及文件：**
- `AIStock.Feature/Services/FeatureStoreService.cs`

**预估工时：** 1-2天

---

### FIX-03: 回测引擎完全不可用

**问题：** `BacktestEngineService` 中 `GetKlinesAsync` 方法返回空列表，整个回测功能无法运行。

**影响：** 前端策略页面配置回测后无法得到任何结果。

**修复方案：**
1. 在 `BacktestEngineService` 中注入 `IDataProviderResolver`
2. 通过 `IDataProvider.GetKlinesAsync` 获取历史K线数据
3. 需要考虑数据量限制（单次最多800条）和分批获取

**涉及文件：**
- `AIStock.Strategy/Services/BacktestEngineService.cs`
- `AIStock.Strategy/DependencyInjection.cs`

**预估工时：** 2-3天

---

## 二、高优先级问题 (High)

### FIX-04: 字符串代替枚举

**问题：** 多处使用字符串表示本应是枚举的值：
- `TradeSignal.SignalType` 是 `string`，但 `SignalType` 枚举已存在
- `RiskCheckResult.RiskLevel` 是 `string`（"critical/high/medium/low"）
- `CredibilityResult.Verdict` 是 `string`（"real/fake/uncertain"）
- `SpreadAnalysisResult.SpreadSpeed` 是 `string`（"viral/fast/medium/slow"）
- `StockCodeInfo.SecurityType` 是 `string`，但 `SecurityType` 枚举已存在

**影响：** 类型不安全，拼写错误导致运行时Bug，IDE无法提供补全和重构支持。

**修复方案：**
1. `TradeSignal.SignalType` 改为 `SignalType` 枚举
2. 新增 `RiskLevel` 枚举，替换 `RiskCheckResult.RiskLevel`
3. 新增 `Verdict` 枚举，替换 `CredibilityResult.Verdict`
4. 新增 `SpreadSpeed` 枚举，替换 `SpreadAnalysisResult.SpreadSpeed`
5. `StockCodeInfo.SecurityType` 改为 `SecurityType` 枚举
6. 将 `SecurityType` 枚举从 `Models/TradeData.cs` 移到 `Enums/` 目录

**涉及文件：**
- `AIStock.Core/Models/Strategy.cs`
- `AIStock.Core/Models/CredibilityAnalysis.cs`
- `AIStock.Core/Models/TradeData.cs`
- `AIStock.Core/Enums/`（新增枚举文件）
- 所有使用这些属性的服务

**预估工时：** 2-3天

---

### FIX-05: Trailing Stop 假实现

**问题：** `StopLossSystemService` 中 `StopLossMode.Trailing` 分支调用的是 `CalculateFixedStopLoss`（8%固定止损），而非实际的移动止盈逻辑。`CalculateTrailingStop` 方法已定义但从未被调用。

**影响：** 动态止盈功能名不副实，用户以为在用移动止盈，实际是固定8%止损。

**修复方案：**
1. 修改 `CalculateStopLoss` 中 `Trailing` 分支，接收最高价参数
2. 调用 `CalculateTrailingStop(highestPrice, trailingPercent)`
3. 更新 `IStopLossSystem` 接口，`CalculateStopLoss` 增加 `highestPrice` 参数（可选）
4. 调用方传入当前最高价

**涉及文件：**
- `AIStock.Risk/Services/StopLossSystemService.cs`
- `AIStock.Core/Interfaces/IStopLossSystem.cs`

**预估工时：** 1天

---

### FIX-06: Sector Concentration 检查无效

**问题：** `RiskEngineService.CheckSectorConcentrationAsync` 实际检查的是总仓位比例，无任何行业分类逻辑。与 `CheckTotalPositionAsync` 完全重复。

**影响：** 板块集中度风控形同虚设，无法防止过度集中于某一行业。

**修复方案：**
1. 在 `StockBase` 中已有 `Industry` 字段
2. 按 `Industry` 分组汇总持仓市值
3. 检查每个行业持仓占总资本比例是否超过 `MaxSectorPercent`
4. 缓存行业分类数据，避免频繁查询

**涉及文件：**
- `AIStock.Risk/Services/RiskEngineService.cs`

**预估工时：** 1-2天

---

### FIX-07: N+1 查询问题

**问题：** 多处循环内单条数据库查询：
- `CompanyRelationGraphService.FindPathsDFS` 每层递归查询DB
- `IndustryChainGraphService.GetCompanyChainPositionsAsync` 每条关联单独查链
- `IndustryChainGraphService.DiffuseConceptAsync` 每家公司单独查StockBase
- `StockFilterService.FilterStocksAsync` 每只股票单独查K线
- `CredibilityAnalyzerService.CheckHistoricalDuplicatesAsync` 加载1000条到内存比较

**影响：** 数据量增大后性能急剧下降，数据库连接池压力大。

**修复方案：**
1. `FindPathsDFS` — 预加载邻接表到内存，DFS在内存中执行
2. `GetCompanyChainPositionsAsync` — 使用 `Include` 或 `Join` 一次查询
3. `DiffuseConceptAsync` — 批量查询公司信息，使用 `Where(x => codes.Contains(x.Code))`
4. `FilterStocksAsync` — 批量获取K线数据
5. `CheckHistoricalDuplicatesAsync` — 使用数据库全文检索或倒排索引

**涉及文件：**
- `AIStock.Knowledge/Services/CompanyRelationGraphService.cs`
- `AIStock.Knowledge/Services/IndustryChainGraphService.cs`
- `AIStock.Knowledge/Services/StockFilterService.cs`
- `AIStock.Intelligence/Services/CredibilityAnalyzerService.cs`

**预估工时：** 3-4天

---

### FIX-08: 全量加载到内存

**问题：** 多处将全表数据加载到内存再处理：
- `EventEngineService.GetStatisticsAsync` 加载全部事件到内存聚合
- `IndustryChainGraphService.DiffuseConceptAsync` 加载全部产业链到内存
- `CredibilityAnalyzerService` 加载1000条事件到内存做字符级相似度

**影响：** 数据量增长后OOM风险，查询延迟线性增加。

**修复方案：**
1. `GetStatisticsAsync` — 使用SQL聚合：
   ```sql
   SELECT Sentiment, COUNT(*), AVG(Importance), AVG(Credibility)
   FROM event_record WHERE EventTime BETWEEN @start AND @end
   GROUP BY Sentiment
   ```
2. `DiffuseConceptAsync` — 使用数据库 `LIKE` 或全文检索过滤
3. 相似度比较 — 改用数据库 `LIKE` 预筛选 + 内存精确比较（缩小候选集）

**涉及文件：**
- `AIStock.EventEngine/Services/EventEngineService.cs`
- `AIStock.Knowledge/Services/IndustryChainGraphService.cs`
- `AIStock.Intelligence/Services/CredibilityAnalyzerService.cs`

**预估工时：** 2-3天

---

### FIX-09: 代码重复

**问题：** 大量重复代码：
- `CleanJsonResponse`（Markdown代码块清理）在4个文件中重复：`ReportAnalyzerAgent`, `PolicyAnalyzerAgent`, `EssayAnalyzerService`, `CredibilityAnalyzerService`
- `GetStringList` 辅助方法在2个文件中重复
- `EventEngineService` 的 `ProcessReportAsync`, `ProcessNewsAsync`, `ProcessPolicyAsync` 方法90%相同
- `ReportAnalyzerAgent` 与 `PolicyAnalyzerAgent` 几乎完全相同

**影响：** 修改一处需要同步修改多处，容易遗漏导致不一致。

**修复方案：**
1. 提取 `LLMResponseParser` 工具类到 `AIStock.Intelligence/Common/`，包含 `CleanJsonResponse`, `GetStringList`, `GetDecimal` 等
2. EventEngine重构为泛型Pipeline：
   ```csharp
   private async Task<EventRecordEntity> ProcessEventAsync(
       string eventType, Func<Task<EventData>> extractor, string title, string? content)
   ```
3. `ReportAnalyzerAgent` 和 `PolicyAnalyzerAgent` 提取公共基类 `BaseLLMAnalyzer`

**涉及文件：**
- `AIStock.Intelligence/Services/` 下多个文件
- `AIStock.EventEngine/Services/EventEngineService.cs`

**预估工时：** 2-3天

---

### FIX-10: 无全局异常处理

**问题：** 每个Controller方法各自 try-catch，错误响应格式不统一（有的返回字符串，有的返回匿名对象）。无 `ProblemDetails` 中间件。

**影响：** 前端无法统一解析错误响应；日志格式不一致；新增Controller需要重复写try-catch。

**修复方案：**
1. 添加全局异常处理中间件：
   ```csharp
   app.UseExceptionHandler(error =>
   {
       error.Run(async context =>
       {
           var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
           context.Response.ContentType = "application/problem+json";
           await context.Response.WriteAsJsonAsync(new ProblemDetails
           {
               Status = 500,
               Title = exception?.Message ?? "Internal Server Error"
           });
       });
   });
   ```
2. 移除各Controller中的try-catch
3. 自定义业务异常类型（如 `BusinessException`, `RiskLimitExceededException`）

**涉及文件：**
- `AIStock.Web/Program.cs`
- `AIStock.Web/Controllers/` 下所有Controller
- `AIStock.Core/` 新增异常类型

**预估工时：** 2天

---

### FIX-11: SignalGeneratorService.GenerateBatchSignalsAsync 无效

**问题：** 批量信号生成方法对每只股票传入空的 `List<TradeSignal>()`，AlphaEngine无法融合任何信号，总是返回 "hold"。

**影响：** 批量决策功能无法生成有效交易信号。

**修复方案：**
1. 为每只股票先调用各策略的 `GenerateSignalAsync` 获取原始信号
2. 将原始信号列表传入 `AlphaEngine.GenerateCompositeSignalAsync`
3. 或者重新设计接口，让批量方法接收策略列表而非预生成的信号

**涉及文件：**
- `AIStock.Execution/Services/SignalGeneratorService.cs`

**预估工时：** 1-2天

---

## 三、中优先级问题 (Medium)

### FIX-12: Worker项目是空壳

**问题：** `AIStock.Worker` 引用了全部13个项目但仅含一个 `Console.WriteLine` 的占位Worker。未配置任何连接字符串或服务注册。

**修复方案：** 二选一：
- **方案A：** 实现后台任务（数据定时采集、策略监控、特征计算调度），补充DI注册和配置
- **方案B：** 移除无用的项目引用，仅保留Core和Infrastructure，标记为待开发

**涉及文件：**
- `AIStock.Worker/Program.cs`
- `AIStock.Worker/Worker.cs`
- `AIStock.Worker/AIStock.Worker.csproj`

**预估工时：** 方案A 5-7天，方案B 0.5天

---

### FIX-13: Monitor项目完全为空

**问题：** `AIStock.Monitor` 仅含 `.csproj` 文件，零实现代码。

**修复方案：** 二选一：
- **方案A：** 实现价格告警、P&L监控、风险预警服务
- **方案B：** 从sln中暂时移除，标记为未来版本

**涉及文件：**
- `AIStock.Monitor/`

**预估工时：** 方案A 5-7天，方案B 0.5天

---

### FIX-14: Agent生命周期管理混乱

**问题：**
- `AgentOrchestrator` 注册为Singleton，但Agents是Scoped
- `OrchestratorController` 构造函数每次请求重新注册Agent到Singleton字典
- 并发请求下可能产生竞态条件

**修复方案：**
1. `AgentOrchestrator` 改为Scoped，或
2. Agent注册移到 `Program.cs` 启动时（使用工厂模式创建Scoped Agent）
3. 移除Controller构造函数中的注册逻辑

**涉及文件：**
- `AIStock.Orchestrator/DependencyInjection.cs`
- `AIStock.Orchestrator/AgentOrchestrator.cs`
- `AIStock.Web/Controllers/OrchestratorController.cs`

**预估工时：** 1-2天

---

### FIX-15: 无输入验证

**问题：** 所有DTO无 `[Required]`, `[StringLength]`, `[Range]` 等验证特性。无效输入直接传入业务层。

**修复方案：**
1. 为核心DTO添加DataAnnotation验证
2. 在 `Program.cs` 添加 `AddControllers().AddDataAnnotations()`
3. 或引入 FluentValidation 包

**涉及文件：**
- `AIStock.Web/Controllers/` 下所有Request DTO
- `AIStock.Web/Program.cs`

**预估工时：** 1-2天

---

### FIX-16: 无重试/熔断机制

**问题：** LLM调用、数据源HTTP请求均无重试逻辑。网络抖动或API限流直接返回失败。

**修复方案：**
1. 引入 Polly NuGet包
2. 为 `IHttpClientFactory` 注册的HttpClient添加重试策略：
   ```csharp
   services.AddHttpClient("default")
       .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(i)));
   ```
3. LLM调用添加指数退避重试
4. 数据源请求添加超时和重试

**涉及文件：**
- `AIStock.LLM/Providers/OpenAICompatibleProvider.cs`
- `AIStock.Data/Providers/BaseProvider.cs`
- `AIStock.Web/Program.cs`

**预估工时：** 2天

---

### FIX-17: 无CancellationToken传播

**问题：** Knowledge和Feature服务大部分方法无 `CancellationToken` 参数。长时间运行的操作无法被取消。

**修复方案：** 为以下接口和实现添加CancellationToken：
- `ICompanyRelationGraph` 所有方法
- `IIndustryChainGraph` 所有方法
- `IStockFilter` 所有方法
- `IFeatureCalculator` 所有方法
- `IFeatureStore` 所有方法

**涉及文件：**
- `AIStock.Core/Interfaces/IKnowledgeGraph.cs`
- `AIStock.Core/Interfaces/IFeatureCalculator.cs`
- `AIStock.Core/Interfaces/IFeatureStore.cs`
- 对应的实现文件

**预估工时：** 1-2天

---

### FIX-18: DTO全部可变

**问题：** 所有Model使用 `{ get; set; }`，无 `record` 类型或 `init` 属性。DTO在传递过程中可被任意修改。

**修复方案：**
1. 纯数据传输对象改为 `record`：
   ```csharp
   public record QuoteData(string Code, string Name, decimal Price, ...);
   ```
2. 或将属性改为 `{ get; init; }`
3. 保留需要反序列化的类使用 `{ get; set; }`

**涉及文件：**
- `AIStock.Core/Models/` 下所有文件

**预估工时：** 2-3天

---

### FIX-19: SanhuProvider 硬编码依赖

**问题：** `OrderManagerService` 直接 `using AIStock.Data.Providers.Sanhu`，通过 `IDataProviderResolver` 解析后强制转换为 `SanhuProvider`。破坏了Provider抽象。

**修复方案：**
1. 在 `IDataProvider` 或 `IAccountProvider` 中增加订单相关方法
2. `SanhuProvider` 实现该接口
3. `OrderManagerService` 通过接口调用，不依赖具体类型

**涉及文件：**
- `AIStock.Core/Interfaces/IDataProvider.cs`
- `AIStock.Data/Providers/Sanhu/SanhuProvider.cs`
- `AIStock.Execution/Services/OrderManagerService.cs`

**预估工时：** 2天

---

### FIX-20: NewsCollectorService 用Playwright调JSON API

**问题：** `NewsCollectorService` 使用Playwright（无头浏览器）调用返回JSON的API，资源消耗大。JSONP解析使用手动字符串切分。

**修复方案：**
1. 将Playwright调用改为 `HttpClient` 直接请求
2. JSONP解析改为正则提取或直接请求非JSONP端点
3. Playwright仅保留给需要渲染页面的场景

**涉及文件：**
- `AIStock.Intelligence/Collectors/NewsCollectorService.cs`

**预估工时：** 1-2天

---

### FIX-21: PlaywrightHelper 每次创建新浏览器

**问题：** `PlaywrightHelper.ExecuteWithBrowserAsync` 每次调用创建新的Playwright实例和浏览器，无复用。

**修复方案：**
1. 使用单例或连接池管理浏览器实例
2. 实现 `IAsyncDisposable` 确保资源释放
3. 考虑使用 BrowserContext 隔离而非 Browser 隔离

**涉及文件：**
- `AIStock.Intelligence/Common/PlaywrightHelper.cs`

**预估工时：** 1-2天

---

### FIX-22: 无SaveChanges拦截器

**问题：** `AIStockDbContext` 未重写 `SaveChanges`，实体的 `CreatedAt`/`UpdatedAt` 需要手动设置。

**修复方案：**
```csharp
public override int SaveChanges()
{
    foreach (var entry in ChangeTracker.Entries<BaseEntity>())
    {
        if (entry.State == EntityState.Added)
            entry.Entity.CreatedAt = DateTime.UtcNow;
        if (entry.State == EntityState.Modified)
            entry.Entity.UpdatedAt = DateTime.UtcNow;
    }
    return base.SaveChanges();
}
```

**涉及文件：**
- `AIStock.Infrastructure/Database/Context/AIStockDbContext.cs`
- 各Entity类添加基类或接口

**预估工时：** 1天

---

### FIX-23: 无Dead Letter Queue

**问题：** `RedisMessageBus` 消息处理失败后仅记录日志，消息永久丢失。无重试、无死信队列。

**修复方案：**
1. 消费失败时将消息推入 `{stream}:dead-letter` 队列
2. 添加重试计数（消息头中记录retry_count）
3. 超过最大重试次数（如3次）转入死信
4. 提供死信队列查询和重放API

**涉及文件：**
- `AIStock.Infrastructure/MessageBus/RedisMessageBus.cs`

**预估工时：** 2天

---

### FIX-24: `GetMarketSentimentAsync` 顺序请求100只股票

**问题：** 市场情绪检测方法顺序获取100只股票行情，耗时长。

**修复方案：**
1. 使用 `IDataProvider.GetQuotesAsync(codes)` 批量接口（已存在于TencentProvider）
2. 或使用 `Task.WhenAll` 并行请求

**涉及文件：**
- `AIStock.Feature/Services/MarketStateDetectorService.cs`

**预估工时：** 0.5天

---

### FIX-25: `EventRecord.RelatedStocks` 逗号分隔存储

**问题：** `EventRecordEntity.RelatedStocks` 和 `RelatedConcepts` 以逗号分隔字符串存储，无法高效查询。

**修复方案：**
1. 新增 `EventStockRelation` 关联表
2. 新增 `EventConceptRelation` 关联表
3. 迁移现有数据
4. 查询改用JOIN

**涉及文件：**
- `AIStock.Infrastructure/Database/Entities/EventRecordEntity.cs`
- `AIStock.Infrastructure/Database/Context/AIStockDbContext.cs`
- `scripts/database/init.sql`

**预估工时：** 2-3天

---

## 四、低优先级问题 (Low)

### FIX-26: 多处魔法数字

**问题：** 代码中大量硬编码数字，无配置或常量说明：
- 风控：50(基础分), 70/30(阈值), 150(强信号), 20%/80%/40%(限制)
- 止损：5%, 8%, 10%, 15%, ATR*2, ATR*3
- 仓位：0.3(假设波动率), 0.15(目标风险), 0.5(Kelly上限)
- 内容截断：3000, 2000, 1000字符

**修复方案：**
1. 风控常量提取到 `RiskConfig` 配置类
2. 策略参数提取到构造函数参数或配置
3. 截断长度提取到 `IntelligenceConfig`

**涉及文件：** 多个服务文件

**预估工时：** 1-2天

---

### FIX-27: `IntradayData.Time` 是string

**问题：** `IntradayData.Time` 类型为 `string`，其他Model均使用 `DateTime`。API响应格式泄露到领域模型。

**修复方案：** 改为 `DateTime` 或 `TimeOnly`，在Provider边界做解析。

**涉及文件：**
- `AIStock.Core/Models/IntradayData.cs`
- 各Provider的分时数据解析逻辑

**预估工时：** 1天

---

### FIX-28: 拼写错误 "controll"

**问题：** `CompanyRelationEntity.RelationType` 中 "controll" 应为 "control"。

**修复方案：** 修正拼写，同步更新数据库init.sql和所有使用处。

**涉及文件：**
- `AIStock.Infrastructure/Database/Entities/CompanyRelationEntity.cs`
- `scripts/database/init.sql`
- `AIStock.Knowledge/Services/CompanyRelationGraphService.cs`

**预估工时：** 0.5天

---

### FIX-29: 空目录清理

**问题：** `AIStock.Core/Extensions/` 和 `AIStock.Infrastructure/Cache/` 目录存在但为空。

**修复方案：** 删除空目录，或实现计划中的扩展方法和缓存服务。

**涉及文件：**
- `AIStock.Core/Extensions/`
- `AIStock.Infrastructure/Cache/`

**预估工时：** 0.5天

---

### FIX-30: TencentProvider 位置解析脆弱

**问题：** 腾讯行情数据使用 `~` 分隔，按数组下标取值（`parts[3]`, `parts[32]`等）。API格式变更会导致解析失败。`decimal.Parse` 无 `TryParse`。

**修复方案：**
1. 所有 `decimal.Parse`/`long.Parse` 改为 `TryParse` + 默认值
2. 添加数组长度校验
3. 记录原始响应用于调试

**涉及文件：**
- `AIStock.Data/Providers/Tencent/TencentProvider.cs`

**预估工时：** 1天

---

### FIX-31: NuGet包版本不一致

**问题：** `Microsoft.AspNetCore.OpenApi` 和 `Microsoft.Extensions.Hosting` 是 `9.0.16`，其他Microsoft包是 `9.0.0`。

**修复方案：**
1. 引入 `Directory.Packages.props` 实现Central Package Management
2. 统一所有Microsoft包到同一版本

**涉及文件：**
- 根目录新增 `Directory.Packages.props`
- 所有 `.csproj` 文件

**预估工时：** 1天

---

### FIX-32: 冗余的 System.Text.Json 引用

**问题：** `AIStock.Data` 和 `AIStock.LLM` 显式引用 `System.Text.Json 9.0.0`，但 .NET 9 运行时已内置。

**修复方案：** 从两个csproj中移除 `System.Text.Json` 引用。

**涉及文件：**
- `AIStock.Data/AIStock.Data.csproj`
- `AIStock.LLM/AIStock.LLM.csproj`

**预估工时：** 0.5天

---

### FIX-33: TdxProvider.Dispose() 同步等待异步释放

**问题：** `TdxProvider.Dispose()` 调用 `_client?.DisposeAsync().AsTask().Wait()`，在同步上下文中可能死锁。

**修复方案：** 实现 `IAsyncDisposable` 接口，使用 `await using` 模式。

**涉及文件：**
- `AIStock.Data/Providers/Tdx/TdxProvider.cs`

**预估工时：** 0.5天

---

### FIX-34: 硬编码TDX服务器IP

**问题：** `TdxProvider` 构造函数默认服务器 `119.147.212.81:7709` 硬编码在代码中。

**修复方案：** 移到 `appsettings.json` 的 `Tdx:Host` 和 `Tdx:Port` 配置项（已有配置结构，但默认值在代码中）。

**涉及文件：**
- `AIStock.Data/Providers/Tdx/TdxProvider.cs`

**预估工时：** 0.5天

---

### FIX-35: 前端API地址硬编码

**问题：** `web/src/api/index.ts` 中 API Base URL 硬编码为 `http://localhost:5172/api`，无环境变量支持。

**修复方案：**
1. 使用 `.env` 文件配置 `REACT_APP_API_BASE_URL`
2. `api/index.ts` 读取环境变量：
   ```typescript
   const BASE_URL = process.env.REACT_APP_API_BASE_URL || 'http://localhost:5172/api';
   ```

**涉及文件：**
- `web/src/api/index.ts`
- `web/.env`（新建）

**预估工时：** 0.5天

---

### FIX-36: 前端大量any类型

**问题：** 前端所有API响应和组件状态使用 `any` 类型，TypeScript类型安全形同虚设。

**修复方案：**
1. 创建 `web/src/types/` 目录
2. 定义API响应接口：`QuoteData`, `PositionSummary`, `AgentStatus`, `BacktestResult` 等
3. 替换所有 `any` 为具体类型

**涉及文件：**
- `web/src/types/`（新建）
- `web/src/pages/` 下所有文件
- `web/src/api/index.ts`

**预估工时：** 2-3天

---

### FIX-37: 前端测试已损坏

**问题：** `App.test.tsx` 查找 "learn react" 文本，但实际App渲染的是中文交易系统UI。测试必然失败。

**修复方案：** 重写测试或暂时移除，待正式补充测试时重建。

**涉及文件：**
- `web/src/App.test.tsx`

**预估工时：** 0.5天

---

## 五、任务排期建议

### 第一周：核心功能修复
| 任务 | 工时 | 依赖 |
|------|------|------|
| FIX-03 回测引擎 | 2-3天 | 无 |
| FIX-05 Trailing Stop | 1天 | 无 |
| FIX-06 Sector Concentration | 1-2天 | 无 |
| FIX-11 批量信号生成 | 1-2天 | 无 |
| FIX-02 Redis KEYS | 1-2天 | 无 |

### 第二周：数据层优化
| 任务 | 工时 | 依赖 |
|------|------|------|
| FIX-01 EF Migration | 2-3天 | 无 |
| FIX-07 N+1查询 | 3-4天 | FIX-01 |
| FIX-08 内存全量加载 | 2-3天 | FIX-07 |

### 第三周：架构改进
| 任务 | 工时 | 依赖 |
|------|------|------|
| FIX-04 枚举替换 | 2-3天 | 无 |
| FIX-09 代码重复 | 2-3天 | 无 |
| FIX-10 全局异常处理 | 2天 | 无 |
| FIX-16 重试熔断 | 2天 | 无 |

### 第四周：接口与配置
| 任务 | 工时 | 依赖 |
|------|------|------|
| FIX-14 Agent生命周期 | 1-2天 | 无 |
| FIX-15 输入验证 | 1-2天 | 无 |
| FIX-19 SanhuProvider解耦 | 2天 | 无 |
| FIX-26 魔法数字 | 1-2天 | 无 |

### 第五周：前端与细节
| 任务 | 工时 | 依赖 |
|------|------|------|
| FIX-35 前端环境变量 | 0.5天 | 无 |
| FIX-36 前端类型 | 2-3天 | 无 |
| FIX-27 Time类型 | 1天 | 无 |
| FIX-28 拼写修正 | 0.5天 | 无 |
| FIX-30 Tencent解析 | 1天 | 无 |
| FIX-31~34 包版本/清理 | 1天 | 无 |

### 后续迭代
| 任务 | 工时 | 备注 |
|------|------|------|
| FIX-12 Worker实现 | 5-7天 | 按需 |
| FIX-13 Monitor实现 | 5-7天 | 按需 |
| FIX-18 DTO改record | 2-3天 | 逐步迁移 |
| FIX-20~21 Playwright优化 | 2-3天 | 按需 |
| FIX-22 SaveChanges拦截 | 1天 | 配合Migration |
| FIX-23 Dead Letter Queue | 2天 | 按需 |
| FIX-25 EventRecord规范化 | 2-3天 | 按需 |

---

## 更新记录

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-05-28 | v1.0 | 初始版本，基于全面代码审查生成37项修复任务 |
