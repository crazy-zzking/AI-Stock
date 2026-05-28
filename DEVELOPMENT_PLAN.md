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
| 行情采集 | 实时行情 | 东方财富（隧道代理）/ 通达信 | P0 | ✅ 完成 |
| K线采集 | 日K/周K/月K/分钟K | 腾讯财经 / 通达信 | P0 | ✅ 完成 |
| 分时采集 | 分时数据 | 散户量化 / 通达信 | P0 | ✅ 完成 |
| 历史分时 | 历史分时数据 | 通达信 | P0 | ✅ 完成 |
| 分笔成交 | 分笔成交数据 | 通达信 | P0 | ✅ 完成 |
| 历史分笔 | 历史分笔成交 | 通达信 | P0 | ✅ 完成 |
| 集合竞价 | 集合竞价明细 | 通达信 | P1 | ✅ 完成 |
| 股票池 | 股票/指数/ETF代码列表 | 通达信 | P0 | ✅ 完成 |
| 研报采集 | 券商研报 | Playwright + 东财/慧博 | P0 | ✅ 完成 |
| 新闻采集 | 财经新闻 | Playwright + HTTP | P0 | ✅ 完成 |
| 政策采集 | 政策文件 | Playwright | P1 | ✅ 完成 |
| 知识星球 | 付费圈内容 | Playwright | P2 | ⏳ 阶段二 |
| 社交媒体 | 雪球/股吧 | Playwright | P2 | ⏳ 阶段三 |

### 第2层：信息情报层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 研报分析Agent | 自动摘要/超预期点/产业方向 | P0 | ✅ 完成 |
| 政策分析Agent | 政策摘要/产业链推演 | P1 | ✅ 完成 |
| 小作文分析Agent | OCR/ASR解析/可信度分析 | P2 | ⏳ 阶段三 |
| 知识星球Agent | 新内容监控/关键词报警 | P2 | ⏳ 阶段三 |

### 第3层：NLP事件引擎

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 多模型管理 | 动态配置BaseURL/ApiKey/Model | P0 | ✅ 完成 |
| 事件抽取 | 公司/产品/时间/利好方向 | P0 | ✅ 完成 |
| 情绪分析 | 利好/利空/中性判断 | P0 | ✅ 完成 |
| 强度评分 | 重磅程度/可信度/传播速度 | P1 | ✅ 完成 |
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
| 参股关系图谱 | 上市公司/未上市龙头参股关系挖掘 | P1 | ⏳ 阶段三 |
| 资金图谱 | 游资席位/联动板块/妖股路径 | P2 | ⏳ 阶段四 |

#### 参股关系图谱功能设计

**功能目标：**
- 挖掘上市公司参股的其他公司（上市/未上市）
- 挖掘未上市行业龙头参股的上市公司
- 自动发现关联概念和题材
- 构建参股关系网络

**数据来源：**
- 企查查/天眼查（工商信息）
- 上市公司年报（对外投资）
- 招股说明书（关联方）
- 新闻公告（投资事件）

**核心能力：**
| 能力 | 说明 |
|------|------|
| 参股关系采集 | 抓取工商信息、年报数据 |
| 关系图谱构建 | 构建公司-参股-公司关系网络 |
| 概念挖掘 | 从参股关系推导概念题材 |
| 龙头识别 | 识别未上市行业龙头 |
| 路径发现 | 发现多层参股路径 |

**示例场景：**
```
宁德时代（上市） → 参股 → 某锂电池材料公司（未上市）
                      ↓
              概念：锂电池、新能源、材料

某AI独角兽（未上市龙头） → 参股 → 某上市公司
                              ↓
                      概念：AI、独角兽、股权投资
```

### 第7层：特征工程层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 传统因子 | MA/MACD/RSI/VWAP/波动率 | P0 | ✅ 完成 |
| AI特征 | Order Flow Embedding/新闻Embedding | P1 | ⏳ 阶段五 |
| 市场状态 | 牛熊/震荡/极端状态识别 | P1 | ✅ 完成 |
| Feature Store | 统一特征存储 | P1 | ✅ 完成 |

### 第8层：策略层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 回测引擎 | 手续费/滑点/冲击成本/涨跌停 | P0 | ✅ 完成 |
| 规则策略 | 均线突破/网格/配对交易 | P0 | ✅ 完成 |
| ML策略 | XGBoost/LightGBM | P1 | ⏳ 阶段五 |
| DL策略 | LSTM/Transformer/PatchTST | P1 | ⏳ 阶段六 |
| RL策略 | PPO/SAC/DQN动态仓位 | P2 | ⏳ 阶段六 |
| Multi-Agent | Research/Theme/Alpha/Risk/Execution | P3 | ⏳ 阶段六 |

### 第9层：风控层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 仓位控制 | Kelly/风险平价/波动率目标 | P0 | ✅ 完成 |
| 止损系统 | ATR止损/固定止损/动态止盈 | P0 | ✅ 完成 |
| 风险暴露 | 单票/板块/Beta/杠杆限制 | P0 | ✅ 完成 |
| 黑天鹅保护 | 熔断/波动率异常/极端行情检测 | P1 | ⏳ 阶段六 |

### 第10层：执行层

| 模块 | 功能 | 优先级 | 状态 |
|------|------|--------|------|
| 信号生成 | 多策略信号融合 | P0 | ✅ 完成 |
| 订单管理 | 下单/撤单/改单（SanhuQuant） | P0 | ✅ 完成 |
| 执行算法 | TWAP/VWAP/冰山单 | P1 | ⏳ 阶段六 |

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
| 通达信Provider | ✅ | 行情/K线/分时/历史分时/分笔成交/集合竞价/股票池 |
| 股票池类型区分 | ✅ | 支持Stock/Index/ETF类型过滤 |
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

| 数据源 | 行情 | K线 | 分时 | 历史分时 | 分笔成交 | 集合竞价 | 股票池 | 代理 |
|--------|------|-----|------|----------|----------|----------|--------|------|
| 东方财富 | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | 隧道代理 |
| 腾讯财经 | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | 不需要 |
| 散户量化 | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ | 不需要 |
| 通达信 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 不需要 |

#### 股票池类型支持

| 类型 | 判断方法 | 示例 |
|------|----------|------|
| Stock | 默认 | 600519, 000001 |
| Index | TdxCode.IsIndex() | 000001(上证), 399001(深证) |
| ETF | TdxCode.IsEtf() | 510300, 159919 |

---

### 阶段二：情报+NLP（已完成 ✅）

**工期：** 4-5周

**目标：** 实现LLM Gateway和情报分析Agent

#### 任务清单

| 任务 | 优先级 | 说明 | 状态 |
|------|--------|------|------|
| LLM Gateway | P0 | 多模型动态配置（BaseURL/ApiKey/Model） | ✅ 完成 |
| 多模型对比 | P0 | 并行调用 + 投票机制 | ✅ 完成 |
| 研报采集 | P0 | Playwright抓取东财/慧博研报 | ✅ 完成 |
| 新闻采集 | P0 | 财经新闻抓取 | ✅ 完成 |
| 研报分析Agent | P0 | 自动摘要/超预期点/产业方向 | ✅ 完成 |
| 事件抽取引擎 | P0 | 公司/产品/时间/利好方向 | ✅ 完成 |
| 情绪分析 | P0 | 利好/利空/中性判断 | ✅ 完成 |
| 政策分析Agent | P1 | 政策摘要/产业链推演 | ✅ 完成 |
| 强度评分 | P1 | 重磅程度/可信度/传播速度 | ✅ 完成 |

#### 已实现API

| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/llm/models` | GET | 获取所有可用模型 |
| `/api/llm/models/{modelId}` | GET | 获取指定模型配置 |
| `/api/llm/models/refresh` | POST | 刷新模型配置缓存 |
| `/api/llm/chat` | POST | 发送LLM请求 |
| `/api/llm/compare` | POST | 多模型对比 |
| `/api/intelligence/reports` | GET | 采集最新研报 |
| `/api/intelligence/reports/{stockCode}` | GET | 采集指定股票研报 |
| `/api/intelligence/reports/analyze` | POST | 分析研报 |
| `/api/intelligence/reports/process` | POST | 处理并保存研报事件 |
| `/api/intelligence/news` | GET | 采集最新新闻 |
| `/api/intelligence/news/{stockCode}` | GET | 采集指定股票新闻 |
| `/api/intelligence/news/category/{category}` | GET | 采集指定类别新闻 |
| `/api/intelligence/news/process` | POST | 处理并保存新闻事件 |
| `/api/intelligence/policy/analyze` | POST | 分析政策 |
| `/api/intelligence/policy/process` | POST | 处理并保存政策事件 |
| `/api/event` | GET | 获取最近事件 |
| `/api/event/{eventId}` | GET | 获取事件详情 |
| `/api/event/search` | GET | 搜索事件 |
| `/api/event/statistics` | GET | 获取事件统计 |
| `/api/event/extract` | POST | 从文本中抽取事件 |
| `/api/event/sentiment` | POST | 分析文本情绪 |
| `/api/event/intensity` | POST | 评估事件强度 |

---

### 阶段三：知识图谱+传播分析（已完成 ✅）

**工期：** 3-4周

**目标：** 实现知识图谱和传播链分析

#### 任务清单

| 任务 | 优先级 | 说明 | 状态 |
|------|--------|------|------|
| 公司关系图谱 | P1 | MySQL存储客户/供应商/控股关系 | ✅ 完成 |
| 产业链图谱 | P1 | 上下游关系建模 | ✅ 完成 |
| 概念扩散引擎 | P0 | 从核心事件推演产业链 | ✅ 完成 |
| 标的筛选器 | P0 | 市值小/弹性大/未启动/机构少 | ✅ 完成 |
| 小作文分析Agent | P2 | OCR/ASR解析/可信度分析 | ✅ 完成 |
| 知识星球抓取 | P2 | Playwright抓取 | ✅ 完成 |
| 真假识别 | P1 | 历史重复/逻辑闭环/资金配合 | ✅ 完成 |
| 传播链分析 | P2 | 首发源/传播路径/热度斜率 | ✅ 完成 |
| 数据源记录 | P0 | 分析结果记录数据来源 | ✅ 完成 |

#### 已实现API

| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/knowledge/company/relation` | POST | 添加公司关系 |
| `/api/knowledge/company/relations` | POST | 批量添加公司关系 |
| `/api/knowledge/company/{code}/relations` | GET | 获取公司关系 |
| `/api/knowledge/company/{code}/suppliers` | GET | 获取供应商 |
| `/api/knowledge/company/{code}/customers` | GET | 获取客户 |
| `/api/knowledge/company/path` | GET | 查找关系路径 |
| `/api/knowledge/chain/node` | POST | 添加产业链节点 |
| `/api/knowledge/chain/company` | POST | 添加公司-产业链关联 |
| `/api/knowledge/chain/{name}` | GET | 获取产业链结构 |
| `/api/knowledge/chain/{name}/companies` | GET | 获取产业链公司 |
| `/api/knowledge/chains` | GET | 获取所有产业链 |
| `/api/knowledge/chain/diffuse` | POST | 概念扩散推演 |
| `/api/knowledge/filter` | POST | 条件筛选标的 |
| `/api/knowledge/filter/concepts` | POST | 概念筛选标的 |
| `/api/knowledge/filter/chain` | POST | 产业链筛选标的 |
| `/api/event/credibility` | POST | 真假识别 |
| `/api/event/spread` | GET | 传播链分析 |
| `/api/intelligence/essay/analyze` | POST | 小作文分析 |
| `/api/intelligence/essay/analyze/image` | POST | 图片分析(OCR) |
| `/api/intelligence/essay/analyze/audio` | POST | 音频分析(ASR) |
| `/api/intelligence/knowledge-star` | GET | 知识星球内容 |
| `/api/intelligence/knowledge-star/search` | GET | 搜索知识星球 |

---

### 阶段四：策略+回测（已完成 ✅）

**工期：** 4-5周

**目标：** 实现回测引擎和基础策略

#### 任务清单

| 任务 | 优先级 | 说明 | 状态 |
|------|--------|------|------|
| Feature Store | P1 | Redis + MySQL统一特征存储 | ✅ 完成 |
| 传统因子计算 | P0 | MA/MACD/RSI/VWAP/波动率 | ✅ 完成 |
| AI特征工程 | P1 | Order Flow Embedding/新闻Embedding | ⏳ 阶段五 |
| 回测引擎 | P0 | 手续费/滑点/冲击成本/涨跌停模拟 | ✅ 完成 |
| 规则策略 | P0 | 均线突破/网格/配对交易 | ✅ 完成 |
| ML策略 | P1 | XGBoost/LightGBM | ⏳ 阶段五 |
| Alpha Engine | P0 | 交易信号生成 | ✅ 完成 |
| Portfolio Engine | P0 | 组合管理/仓位分配 | ✅ 完成 |
| Risk Engine | P0 | 风控检查 | ✅ 完成 |
| 仓位控制 | P0 | Kelly/风险平价/波动率目标 | ✅ 完成 |
| 止损系统 | P0 | ATR止损/固定止损/动态止盈 | ✅ 完成 |

#### 已实现API

| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/feature/{code}/indicators` | GET | 计算股票技术指标 |
| `/api/feature/{code}/latest` | GET | 获取最新特征 |
| `/api/feature/{code}/history` | GET | 获取历史特征 |
| `/api/feature/market/state` | GET | 检测市场状态 |
| `/api/feature/market/sentiment` | GET | 获取市场情绪 |
| `/api/feature/market/extreme` | GET | 检测极端行情 |
| `/api/strategy/backtest` | POST | 运行回测 |
| `/api/strategy/signal/merge` | POST | 信号融合 |
| `/api/strategy/portfolio/positions` | POST | 计算目标仓位 |
| `/api/strategy/portfolio/rebalance` | POST | 组合再平衡 |
| `/api/risk/check` | POST | 风控检查 |
| `/api/risk/position-size` | POST | 计算仓位大小 |
| `/api/risk/kelly` | POST | Kelly公式计算 |
| `/api/risk/stop-loss` | POST | 计算止损价 |
| `/api/risk/take-profit` | POST | 计算止盈价 |
| `/api/risk/atr-stop-loss` | POST | ATR止损计算 |
| `/api/risk/fixed-stop-loss` | POST | 固定止损计算 |
| `/api/risk/trailing-stop` | POST | 动态止盈计算 |
| `/api/execution/order` | POST | 下单 |
| `/api/execution/order/{orderId}` | DELETE | 撤单 |
| `/api/execution/order/{orderId}/status` | GET | 查询订单状态 |
| `/api/execution/orders` | GET | 获取订单列表 |

---

### 阶段五：风控+执行（部分完成）

**工期：** 3-4周

**目标：** 实现风控系统和交易执行

#### 任务清单

| 任务 | 优先级 | 说明 | 状态 |
|------|--------|------|------|
| Risk Engine | P0 | 风控引擎核心 | ✅ 完成 |
| 仓位控制 | P0 | Kelly/风险平价/波动率目标 | ✅ 完成 |
| 止损系统 | P0 | ATR止损/固定止损/动态止盈 | ✅ 完成 |
| 风险暴露控制 | P0 | 单票/板块/Beta/杠杆限制 | ✅ 完成 |
| 黑天鹅保护 | P1 | 熔断/波动率异常检测 | ⏳ 阶段六 |
| OMS订单管理 | P0 | 下单/撤单/改单/状态同步 | ✅ 完成 |
| 执行算法 | P1 | TWAP/VWAP/冰山单 | ⏳ 阶段六 |
| SanhuQuant集成 | P0 | 实盘交易接口对接 | ⏳ 阶段六 |

---

### 阶段六：AI高级能力（待开发）

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

---

### 阶段七：Web平台+监控（待开发）

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
| 阶段二 | 4-5周 | 9周 | ✅ 完成 |
| 阶段三 | 3-4周 | 13周 | ✅ 完成 |
| 阶段四 | 4-5周 | 18周 | ✅ 完成 |
| 阶段五 | 3-4周 | 22周 | ⏳ 部分完成 |
| 阶段六 | 4-5周 | 27周 | ⏳ 待开发 |
| 阶段七 | 3-4周 | 31周 | ⏳ 待开发 |
| **总计** | **28-35周** | | |

---

## 更新记录

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-05-28 | v1.0 | 初始版本，完成阶段一 |
| 2026-05-28 | v1.1 | 通达信数据源完整实现（行情/K线/分时/历史分时/分笔成交/集合竞价/股票池） |
| 2026-05-28 | v1.2 | 新增数据能力枚举：HistoryIntraday/Trades/HistoryTrades/CallAuction |
| 2026-05-28 | v1.3 | 股票池支持类型区分：Stock/Index/ETF |
| 2026-05-28 | v1.4 | 新增参股关系图谱功能设计（上市公司/未上市龙头参股关系挖掘） |
| 2026-05-28 | v1.5 | 修正阶段划分：新增阶段五（风控+执行），原阶段五改为阶段六，原阶段六改为阶段七 |
| 2026-05-28 | v2.0 | 完成阶段二：情报+NLP（LLM Gateway、研报/新闻采集、事件抽取、情绪分析、强度评分） |
| 2026-05-28 | v3.0 | 完成阶段三：知识图谱+传播分析（公司关系图谱、产业链图谱、概念扩散、标的筛选、真假识别、传播链分析、小作文分析、数据源记录） |
| 2026-05-28 | v4.0 | 完成阶段四：策略+回测（Feature Store、传统因子计算、回测引擎、规则策略、Alpha Engine、Portfolio Engine、Risk Engine、仓位控制、止损系统） |
| 2026-05-28 | v4.1 | 对接散户量化持仓和订单接口（持仓查询、买入、卖出、订单查询、可撤委托、成交记录） |
