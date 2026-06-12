# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 开发准则

### 1. 不擅自决策功能范围
- 任何新功能、重构、技术方案，**必须先征得用户明确同意**，不得自行决定开发什么
- 用户未明确要求的功能，即使认为有用，也不得顺带实现
- 发现潜在问题或改进点，提出建议，等待确认后再动手

### 2. 开发前必须输出开发计划
每次开发任务开始前，先列出计划，格式如下：

```
## 开发计划：[功能名]

### 目标
[一句话说明要做什么]

### 实现步骤
- [ ] 步骤 1
- [ ] 步骤 2
- [ ] ...

### 涉及文件
[列出预计改动的文件]

### 风险 / 注意点
[潜在影响或需要注意的地方]
```

计划确认后才开始写代码，过程中随完成进度更新 `- [x]` / `- [ ]`。

### 3. 提交前必须测试验收
- 代码写完后，**必须实际运行验证**（启动服务、调用接口、观察日志），不能仅凭代码逻辑判断"应该没问题"
- 验收通过后在计划中标记全部步骤完成，再执行 `git commit`
- 若无法自动化测试，明确说明手动验收了哪些场景、结果是什么

---

## 项目概述

AI 自主交易系统（AI Autonomous Trading OS）。.NET 9 后端 + React 前端，面向 A 股短线埋伏型选股与自主交易。

## 常用命令

### 后端

```bash
# 启动 Web API（http://localhost:5172）
cd AIStock.Web && dotnet run

# 启动后台 Worker（数据同步、情报采集）
cd AIStock.Worker && dotnet run

# 构建整个解决方案
dotnet build AI-Stock.sln

# 数据库结构由 EF Core 迁移管理，Web/Worker 启动时自动 Database.Migrate()
# 新增表/列：dotnet ef migrations add <Name> --project AIStock.Infrastructure --startup-project AIStock.Web
# 注意：migrations add 只生成代码，必须再 build 一次否则运行中的 exe 不含新迁移
```

### 前端

```bash
cd web
npm install
npm start    # 开发服务器（默认 http://localhost:3000）
npm run build
```

### Docker

```bash
docker-compose up -d   # 同时启动 Web + Worker + MySQL + Redis
```

## 配置

用户私有配置写在各项目的 `appsettings.user.json`（已加入 .gitignore），覆盖 `appsettings.json` 中的占位值：
- `AIStock.Web/appsettings.user.json` — API 密钥、数据库连接、LLM 配置
- `AIStock.Worker/appsettings.user.json` — Worker 专用调度参数

关键配置项：
- `Trading.Mode`：`DryRun`（模拟）/ `Live`（实盘），**默认 DryRun**
- `Trading.Halted`：全局熔断开关
- `LLM.Models`：支持通义千问 / DeepSeek / 本地 Ollama，按 Priority 顺序回退
- `DataProviders.Sanhu.Token`：散户量化 Token（K 线数据源）

## 整体架构

### 项目分层

```
AIStock.Core          # 接口定义 + 领域模型 + 枚举（无外部依赖）
AIStock.Infrastructure # EF Core MySQL + Redis + 消息总线（Redis Stream）
AIStock.Data          # 数据源 Provider：Eastmoney / Sanhu / Tencent / Tdx
AIStock.LLM           # LLM 多模型路由 + ILLMService 实现
AIStock.Intelligence  # 情报采集（新闻/公告/研报/知识星球）+ 事件抽取
AIStock.EventEngine   # GraphRAG 候选边管理 + 晋升调度
AIStock.GraphRAG      # 知识图谱（公司关系/行业链）
AIStock.Knowledge     # 知识星球采集
AIStock.Feature       # 技术因子计算（RSI/MACD/均线等）
AIStock.Selection     # 选股引擎（两级漏斗 + 大盘环境 + 多因子打分）
AIStock.Strategy      # 策略层（信号生成）
AIStock.Risk          # 风控（止损/仓位/风险引擎）
AIStock.Execution     # 执行层（订单管理/持仓管理/TradingGate）
AIStock.Orchestrator  # AI Agent 编排（ReflectiveAgent / AlphaAgent / RiskAgent / ResearchAgent）
AIStock.Monitor       # 账户/持仓监控 + Webhook 告警
AIStock.Memory        # Agent 记忆持久化
AIStock.Prompt        # Prompt 模板管理
AIStock.Web           # ASP.NET Core Web API（入口）
AIStock.Worker        # 后台定时任务（数据同步、情报采集、每日选股、绩效补算）
AIStock.Mcp           # MCP 工具服务端（把系统能力暴露给外部 AI 客户端）
web/                  # React + Ant Design 前端
```

### 数据流

1. **数据采集**（Worker）：`DataSyncService` 拉取 K 线 → `MarketSnapshotSyncService` 盘中每 ~20 分钟用腾讯批量快照刷新 `daily_market_snapshot` → `DragonTigerSyncService` 龙虎榜 → `CapitalFlowSyncService` 资金流 → `IndexKlineSyncService` 指数日K
2. **情报采集**（Worker）：`IntelligenceSyncService` 采集新闻/公告/研报/知识星球小作文 → LLM 抽取事件（情绪/重要性/可信度）→ `EventEngine` 写候选边 → `graph-promote` 任务按阈值晋升进权威图谱
3. **每日自动选股**（Worker）：`selection-premarket`（交易日 09:00，昨收数据+隔夜情报）与 `selection-daily`（14:50 尾盘快照后）全策略选股留痕并推送报告；手动选股走 `/api/selection/run`。选股纯 DB + 内存：两级漏斗 → 多因子打分 → 题材生命周期退潮剔除 → RegimeGate 出手闸门 → TOP-N 写 `selection_result`（固化策略键+配置版本）
4. **绩效归因**（Worker）：`selection-performance`（17:00）把每日信号物化进 `selection_performance`，随 K 线补算 T+1/3/5 收益与沪深300超额——前端「策略记分板」按 策略×配置版本×大盘环境 考核，LLM 复评价值经 `/review-stats` 考核
5. **自主决策**（`Orchestrator`）：`AutonomousDecisionSystem` 协调 AlphaAgent / RiskAgent / ResearchAgent → 生成交易信号 → `TradingGate` 安全检查 → `OrderManager` 下单；尾盘自动交易（`TailBuy`，默认关闭）14:55 自动买入/到期卖出/盘中止损
6. **前端**：React SPA，通过 Axios 调用后端 REST API，关键页面用 `keepalive-for-react` 保活 Tab

### 关键设计约定

- **消息总线**：Redis Stream，接口 `IMessageBus`，Infrastructure 层实现
- **数据源解析**：`IDataProviderResolver` 根据股票代码前缀路由到对应 Provider
- **LLM 路由**：`ILLMService` 按优先级依次尝试，全部失败才抛出
- **TradingGate**：所有下单必须经过，检查 `Halted`、模式（DryRun 记录但不实际发单）、单笔限额、日单数、个股暂停；熔断/日单数/暂停名单经 `trading_gate_state` 持久化，重启不丢失
- **选股历史留存**：每次选股追加 `selection_result`（固化策略键、显示名、配置版本），不覆盖，可按日期回溯
- **Worker 调度**：每个 Job 独立配置 `Enabled / RunOnStartup / DailyAtHour(+Minute) / IntervalSeconds`；**配置以 DB worker_config 为权威、appsettings 仅首次种子**（DB 已有 Jobs 段时新任务种子不会自动合并，需经 `PUT /api/workerconfig/jobs` 写入）；运行态落 `worker_job_run`（单飞锁防并发、失败经 Webhook 告警、前端可手动「立即运行」）
- **回测两套口径**：`ReplayBacktestService` 回放=用当前代码重跑历史（调参验证，实盘化口径：一字板剔除/跌停顺延/摩擦）；`selection_performance` 记分板=真实留痕的不可篡改成绩单（改策略后历史不变，按配置版本分段）

### 主要数据库表

| 表 | 用途 |
|---|---|
| `stock_base` | 股票基本信息（代码/名称/行业/市值） |
| `kline_data` | 日 K 线 |
| `daily_market_snapshot` | 盘中/收盘快照（选股主数据） |
| `dragon_tiger` / `dragon_tiger_seat` | 龙虎榜 |
| `stock_concept_relation` | 个股概念/题材 |
| `selection_result` | 选股历史结果（含策略键/配置版本） |
| `selection_performance` | 选股信号前向绩效（T+1/3/5 收益、超额、环境、版本——记分板数据源） |
| `selection_config` | 选股配置（版本化，激活版本生效） |
| `event_record` + `event_relation_*` | 情报事件及关系 |
| `graph_candidate_edge` | 待晋升的图谱候选边 |
| `position` / `trade_record` | 持仓与交易记录 |
| `tail_position` | 尾盘自动交易持仓（开仓固化 HoldDays/止损价） |
| `trading_gate_state` | 交易闸门持久化状态（单行：熔断/日单数/个股暂停） |
| `worker_config` / `worker_job_run` | Worker 任务配置（DB 权威）与运行态 |
| `agent_memory` | Agent 记忆 |
| `prompt_template` / `llm_model_config` | Prompt 与模型配置（DB 管理） |

### 前端路由（`web/src/pages/`）

Dashboard / Selection / SelectionHistory / SelectionConfig / StrategyManager / Backtest（选股回测）/ StrategyScoreboard（策略记分板）/ StockDetail / Sector / AutoTrading / Positions / Strategy / Review / KnowledgeGraph / AIChat / Agents / AgentMemory / LLMManager / PromptManager / Observability / WorkflowEditor / WorkerConfig（任务配置）
