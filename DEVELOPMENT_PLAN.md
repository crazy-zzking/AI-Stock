# AI-Stock 开发计划

## 项目概述

AI自主交易系统（AI Autonomous Trading OS），集AI投研、AI情报分析、AI自主交易、AI风控于一体的统一操作系统。

**技术栈：** .NET 9 + React + MySQL 8.0 + Redis 6.2.6

---

## 系统架构

```
数据源层 → DataBus(Redis Stream) → 情报Agent层 → NLP/LLM Gateway
    ↓
事件抽取引擎 → 知识图谱层 → Feature Store → Alpha Engine
    ↓
Portfolio Engine → Risk Engine → OMS → Execution Engine → SanhuQuant API
```

---

## 功能模块清单（11层）

### 第1层：数据采集层

| 模块 | 功能 | 数据源 | 优先级 | 状态 |
|------|------|--------|--------|------|
| 行情采集 | 实时行情 | 东方财富（隧道代理） | P0 | ✅ 完成 |
| K线采集 | 日K/周K/月K | 腾讯财经 | P0 | ✅ 完成 |
| 分时采集 | 分时数据 | 散户量化 | P0 | ✅ 完成 |
| 研报采集 | 券商研报 | Playwright + 东财/慧博 | P0 | ⏳ 阶段二 |
| 新闻采集 | 财经新闻 | Playwright + HTTP | P0 | ⏳ 阶段二 |
| 政策采集 | 政策文件 | Playwright | P1 | ⏳ 阶段二 |
| 知识星球 | 付费圈内容 | Playwright | P2 | ⏳ 阶段二 |
| 社交媒体 | 雪球/股吧 | Playwright | P2 | ⏳ 阶段三 |

### 第2层：信息情报层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 研报分析Agent | 自动摘要/超预期点/产业方向 | P0 | ⏳ 阶段二 |
| 政策分析Agent | 政策摘要/产业链推演 | P1 | ⏳ 阶段二 |
| 小作文分析Agent | OCR/ASR解析/可信度分析 | P2 | ⏳ 阶段三 |
| 知识星球Agent | 新内容监控/关键词报警 | P2 | ⏳ 阶段三 |

### 第3层：NLP事件引擎

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 多模型管理 | 动态配置BaseURL/ApiKey/Model | P0 | ⏳ 阶段二 |
| 事件抽取 | 公司/产品/时间/利好方向 | P0 | ⏳ 阶段二 |
| 情绪分析 | 利好/利空/中性判断 | P0 | ⏳ 阶段二 |
| 强度评分 | 重磅程度/可信度/传播速度 | P1 | ⏳ 阶段二 |
| 时效性分析 | 信息发现速度评估 | P1 | ⏳ 阶段三 |

### 第4层：传播链分析

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 真假识别 | 历史重复/逻辑闭环/资金配合 | P1 | ⏳ 阶段三 |
| 首发源识别 | 最早消息源定位 | P2 | ⏳ 阶段三 |
| 传播路径 | 平台扩散路径追踪 | P2 | ⏳ 阶段三 |
| 热度斜率 | 热度增长速度监测 | P2 | ⏳ 阶段三 |

### 第5层：产业链推演

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 概念扩散引擎 | 从核心事件推演产业链 | P0 | ⏳ 阶段三 |
| 标的筛选器 | 市值小/弹性大/未启动/机构少 | P0 | ⏳ 阶段三 |
| 产业链图谱 | 上下游关系建模 | P1 | ⏳ 阶段三 |

### 第6层：知识图谱

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 公司关系图谱 | 客户/供应商/控股/投资 | P1 | ⏳ 阶段三 |
| 产业链图谱 | 行业上下游完整链条 | P1 | ⏳ 阶段三 |
| 资金图谱 | 游资席位/联动板块/妖股路径 | P2 | ⏳ 阶段四 |

### 第7层：特征工程层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 传统因子 | MA/MACD/RSI/VWAP/波动率 | P0 | ⏳ 阶段四 |
| AI特征 | Order Flow Embedding/新闻Embedding | P1 | ⏳ 阶段四 |
| 市场状态 | 牛熊/震荡/极端状态识别 | P1 | ⏳ 阶段四 |
| Feature Store | 统一特征存储 | P1 | ⏳ 阶段四 |

### 第8层：策略层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 回测引擎 | 手续费/滑点/冲击成本/涨跌停 | P0 | ⏳ 阶段四 |
| 规则策略 | 均线突破/网格/配对交易 | P0 | ⏳ 阶段四 |
| ML策略 | XGBoost/LightGBM | P1 | ⏳ 阶段四 |
| DL策略 | LSTM/Transformer/PatchTST | P1 | ⏳ 阶段五 |
| RL策略 | PPO/SAC/DQN动态仓位 | P2 | ⏳ 阶段五 |
| Multi-Agent | Research/Theme/Alpha/Risk/Execution | P3 | ⏳ 阶段五 |

### 第9层：风控层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 仓位控制 | Kelly/风险平价/波动率目标 | P0 | ⏳ 阶段四 |
| 止损系统 | ATR止损/固定止损/动态止盈 | P0 | ⏳ 阶段四 |
| 风险暴露 | 单票/板块/Beta/杠杆限制 | P0 | ⏳ 阶段四 |
| 黑天鹅保护 | 熔断/波动率异常/极端行情检测 | P1 | ⏳ 阶段五 |

### 第10层：执行层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 信号生成 | 多策略信号融合 | P0 | ⏳ 阶段四 |
| 订单管理 | 下单/撤单/改单（SanhuQuant） | P0 | ⏳ 阶段四 |
| 执行算法 | TWAP/VWAP/冰山单 | P1 | ⏳ 阶段五 |

### 第11层：监控层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| Web Dashboard | PnL/持仓/信号/日志 | P0 | ⏳ 阶段六 |
| 告警系统 | 企业微信/钉钉/Telegram | P1 | ⏳ 阶段六 |
| 模型监控 | 漂移检测/性能衰减 | P2 | ⏳ 阶段六 |

---

## 开发阶段规划

### 阶段一：基础设施（已完成 ✅）

**工期：** 3-4周

**目标：** 搭建项目基础架构，实现数据采集和基础API

#### 完成内容

| 任务 | 状态 | 说明 |
|------|------|------|
| .NET 9 解决方案 | ✅ | 15个项目，完整分层架构 |
| MySQL数据库设计 | ✅ | 9张核心表 |
| Redis Stream消息总线 | ✅ | 支持发布/订阅/消费者组 |
| 数据源Provider接口 | ✅ | 统一IDataProvider接口 |
| 东方财富Provider | ✅ | 行情/K线/分时（隧道代理） |
| 腾讯财经Provider | ✅ | 行情/K线 |
| 散户量化Provider | ✅ | 分时/交易日历/股票池 |
| 通达信Provider | ⚠️ | 需要TCP连接，暂未实现 |
| 基础Web API | ✅ | StockController + HealthController |
| Docker环境 | ✅ | Dockerfile + docker-compose.yml |

#### 已实现API

| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/health` | GET | 健康检查 |
| `/api/stock/list` | GET | 股票列表 |
| `/api/stock/{code}/quote` | GET | 实时行情（东方财富） |
| `/api/stock/{code}/kline` | GET | K线数据（支持provider参数） |
| `/api/stock/{code}/intraday` | GET | 分时数据（散户量化） |
| `/api/stock/providers/status` | GET | 数据源状态 |

#### 数据源分工

| 数据源 | 行情 | K线 | 分时 | 代理 |
|--------|------|-----|------|------|
| 东方财富 | ✅ | ✅ | ✅ | 隧道代理 |
| 腾讯财经 | ✅ | ✅ | ❌ | 不需要 |
| 散户量化 | ❌ | ❌ | ✅ | 不需要 |

---

### 阶段二：情报+NLP（待开发）

**工期：** 4-5周

**目标：** 实现LLM Gateway和情报分析Agent

#### 任务清单

| 任务 | 优先级 | 说明 |
|------|--------|------|
| LLM Gateway | P0 | 多模型动态配置（BaseURL/ApiKey/Model） |
| 多模型对比 | P0 | 并行调用 + 投票机制 |
| 研报采集 | P0 | Playwright抓取东财/慧博研报 |
| 新闻采集 | P0 | 财经新闻抓取 |
| 研报分析Agent | P0 | 自动摘要/超预期点/产业方向 |
| 事件抽取引擎 | P0 | 公司/产品/时间/利好方向 |
| 情绪分析 | P0 | 利好/利空/中性判断 |
| 政策分析Agent | P1 | 政策摘要/产业链推演 |
| 强度评分 | P1 | 重磅程度/可信度/传播速度 |

---

### 阶段三：知识图谱+传播分析（待开发）

**工期：** 3-4周

**目标：** 实现知识图谱和传播链分析

#### 任务清单

| 任务 | 优先级 | 说明 |
|------|--------|------|
| 公司关系图谱 | P1 | MySQL存储客户/供应商/控股关系 |
| 产业链图谱 | P1 | 上下游关系建模 |
| 概念扩散引擎 | P0 | 从核心事件推演产业链 |
| 标的筛选器 | P0 | 市值小/弹性大/未启动/机构少 |
| 小作文分析Agent | P2 | OCR/ASR解析/可信度分析 |
| 知识星球抓取 | P2 | Playwright抓取 |
| 真假识别 | P1 | 历史重复/逻辑闭环/资金配合 |
| 传播链分析 | P2 | 首发源/传播路径/热度斜率 |

---

### 阶段四：策略+回测（待开发）

**工期：** 4-5周

**目标：** 实现回测引擎和基础策略

#### 任务清单

| 任务 | 优先级 | 说明 |
|------|--------|------|
| Feature Store | P1 | Redis + MySQL统一特征存储 |
| 传统因子计算 | P0 | MA/MACD/RSI/VWAP/波动率 |
| AI特征工程 | P1 | Order Flow Embedding/新闻Embedding |
| 回测引擎 | P0 | 手续费/滑点/冲击成本/涨跌停模拟 |
| 规则策略 | P0 | 均线突破/网格/配对交易 |
| ML策略 | P1 | XGBoost/LightGBM |
| Alpha Engine | P0 | 交易信号生成 |
| Portfolio Engine | P0 | 组合管理/仓位分配 |
| Risk Engine | P0 | 风控检查 |
| 仓位控制 | P0 | Kelly/风险平价/波动率目标 |
| 止损系统 | P0 | ATR止损/固定止损/动态止盈 |

---

### 阶段五：AI高级能力（待开发）

**工期：** 4-5周

**目标：** 实现深度学习、强化学习和Multi-Agent

#### 任务清单

| 任务 | 优先级 | 说明 |
|------|--------|------|
| DL策略 | P1 | LSTM/Transformer/PatchTST |
| RL策略 | P2 | PPO/SAC/DQN动态仓位 |
| Multi-Agent框架 | P3 | Research/Theme/Alpha/Risk/Execution |
| Agent编排器 | P3 | 工作流编排 |
| 自主决策系统 | P3 | 自动交易决策 |
| 黑天鹅保护 | P1 | 熔断/波动率异常检测 |
| 执行算法 | P1 | TWAP/VWAP/冰山单 |

---

### 阶段六：Web平台+监控（待开发）

**工期：** 3-4周

**目标：** 实现React管理界面和监控系统

#### 任务清单

| 任务 | 优先级 | 说明 |
|------|--------|------|
| React项目初始化 | P0 | React + Ant Design Pro |
| Dashboard页面 | P0 | PnL/持仓/信号/日志 |
| 策略管理界面 | P0 | 策略配置/回测/启动 |
| 持仓收益查看 | P0 | 实时持仓/盈亏 |
| 多模型对比界面 | P1 | LLM模型对比 |
| 告警系统 | P1 | 企业微信/钉钉/Telegram |
| 模型监控 | P2 | 漂移检测/性能衰减 |
| Docker部署优化 | P1 | 生产环境配置 |

---

## 项目结构

```
AI-Stock/
├── AI-Stock.sln
├── src/
│   ├── AIStock.Core/           # 核心接口和模型
│   │   ├── Enums/              # 枚举类型
│   │   ├── Interfaces/         # 接口定义
│   │   └── Models/             # 数据模型
│   ├── AIStock.Infrastructure/ # 基础设施
│   │   ├── Cache/              # Redis缓存
│   │   ├── Database/           # MySQL + EF Core
│   │   └── MessageBus/         # Redis Stream
│   ├── AIStock.Data/           # 数据源层
│   │   └── Providers/          # 数据源Provider
│   │       ├── Eastmoney/      # 东方财富
│   │       ├── Tencent/        # 腾讯财经
│   │       ├── Sanhu/          # 散户量化
│   │       └── Tdx/            # 通达信
│   ├── AIStock.Intelligence/   # 情报层
│   ├── AIStock.LLM/            # LLM Gateway
│   ├── AIStock.EventEngine/    # 事件引擎
│   ├── AIStock.Knowledge/      # 知识图谱
│   ├── AIStock.Feature/        # 特征层
│   ├── AIStock.Strategy/       # 策略层
│   ├── AIStock.Risk/           # 风控层
│   ├── AIStock.Execution/      # 执行层
│   ├── AIStock.Orchestrator/   # 编排层
│   ├── AIStock.Monitor/        # 监控层
│   ├── AIStock.Web/            # Web API
│   └── AIStock.Worker/         # 后台服务
├── web/                        # 前端项目（待创建）
├── scripts/
│   └── database/
│       └── init.sql            # 数据库初始化脚本
├── Dockerfile                  # API容器配置
├── Dockerfile.worker           # Worker容器配置
├── docker-compose.yml          # 容器编排
└── DEVELOPMENT_PLAN.md         # 本文档
```

---

## 数据库设计

### 核心表

| 表名 | 说明 | 状态 |
|------|------|------|
| stock_base | 股票基础信息 | ✅ |
| kline_data | K线数据 | ✅ |
| company_relation | 公司关系 | ✅ |
| industry_chain | 产业链 | ✅ |
| company_chain_relation | 公司-产业链关联 | ✅ |
| llm_model_config | LLM模型配置 | ✅ |
| event_record | 事件记录 | ✅ |
| trade_record | 交易记录 | ✅ |
| position | 持仓记录 | ✅ |

---

## 配置说明

### appsettings.user.json

```json
{
  "ConnectionStrings": {
    "MySQL": "Server=localhost;Port=3306;Database=aistock;User=root;Password=xxx;",
    "Redis": "localhost:6379,password=xxx"
  },
  "DataProviders": {
    "Sanhu": {
      "BaseUrl": "http://www.sanhulianghua.com:2008",
      "Token": "your_token"
    }
  },
  "Proxy": {
    "UseTunnelProxy": true,
    "TunnelHost": "c360.kdltps.com",
    "TunnelPort": 15818,
    "TunnelUsername": "xxx",
    "TunnelPassword": "xxx"
  }
}
```

---

## 启动命令

```bash
# 1. 初始化数据库
mysql -u root -p < scripts/database/init.sql

# 2. 启动项目
cd AIStock.Web
dotnet run

# 3. 验证服务
curl http://localhost:5172/api/health
```

---

## 总工期估算

| 阶段 | 工期 | 累计 | 状态 |
|------|------|------|------|
| 阶段一 | 3-4周 | 4周 | ✅ 完成 |
| 阶段二 | 4-5周 | 9周 | ⏳ 待开发 |
| 阶段三 | 3-4周 | 13周 | ⏳ 待开发 |
| 阶段四 | 4-5周 | 18周 | ⏳ 待开发 |
| 阶段五 | 4-5周 | 23周 | ⏳ 待开发 |
| 阶段六 | 3-4周 | 27周 | ⏳ 待开发 |
| **总计** | **24-31周** | | |

---

## 更新记录

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-05-28 | v1.0 | 初始版本，完成阶段一 |
