# 下单查单 + 券商拒单重试 — 改造报告

## 变更动因
用户反馈 `ssjy_jimairu` 实际下单时券商返回 `ret=201`（`不支持的委托类别`），需要：
1. 下单后支持查询 `jycx_chadan` 获取订单的详细状态（含证券系统的拒绝原因 `tip`）
2. 若订单被券商 OMS 拒绝，候选池允许重新下单

## 改动范围（6 个文件）

### 1. SanhuProvider.cs — 响应解析增强
- `SanhuOrderResult` 新增 `Tip` 属性（券商拒绝详因）+ `IsBrokerRejected`（`ret == 201`）
- 新增统一解析方法 `ParseSanhuOrderResult()`：兼容 root 层字段 + `user` 嵌套对象回退
  - `jimairu`/`jimaichu` 失败时 code/price/hand/type/policy 都在 `user` 对象内
- `PlaceBuyOrderAsync` / `PlaceSellOrderAsync` / `QueryOrderSanhuAsync` 全部改用新解析方法
- `QueryOrderAsync`（IDataProvider override）透传 `IsBrokerRejected` + `BrokerTip`

### 2. IDataProvider.cs — 接口扩展
- `TradingOrderResult` 新增 `BrokerTip` 字段
- `TradingOrderStatus` 新增 `IsBrokerRejected` + `BrokerTip` 字段

### 3. IOrderManager.cs — 接口扩展
- `OrderResult` 新增 `BrokerTip` + `IsBrokerRejected` 字段
- 新增 `GetOrderDetailAsync(string orderId)` 方法（返回含 tip 的完整 OrderResult）

### 4. OrderManagerService.cs — 实现 GetOrderDetailAsync
- 调用 `provider.QueryOrderAsync` → 返回含 tip/IsBrokerRejected 的 `OrderResult`
- `GetOrderStatusAsync` 重构为委托 `GetOrderDetailAsync`
- `PlaceOrderAsync` 透传 `BrokerTip` + `IsBrokerRejected`

### 5. TradeCandidateController.cs — check-order + 重下单逻辑
- **新增** `POST /api/trade-candidate/{id}/check-order`：
  - 调用 `GetOrderDetailAsync` 查 chadan
  - 若 `IsBrokerRejected == true`，自动重置候选 status=0（允许重新下单）
  - 返回完整状态 + `brokerTip` 详情
- **修改** `POST /api/trade-candidate/{id}/order`：
  - 若候选 status=1，先查 chadan 确认状态
  - 若上一单已被券商拒绝 → 自动重置 status=0 → 允许重新下单
  - 券商拒单时仍记录 orderId（不丢追踪），但 status 保持 0
  - 返回新增 `brokerTip` / `brokerRejected` 字段

### 6. AIStock.Orchestrator.csproj — 已有引用（无变化）

## 完整流程

```
用户在前端点击"下单"
  → POST /api/trade-candidate/{id}/order
  → 如有旧订单 → GetOrderDetailAsync(chadan) → 被拒则重置
  → PlaceOrderAsync → SanhuProvider.PlaceBuyOrderAsync → ssjy_jimairu
  → 返回 { success, orderId, brokerTip, brokerRejected }
  → 若 brokerRejected=true → 前端显示拒绝原因 → 用户可调整价格后重新下单

用户在前端点击"查单"
  → POST /api/trade-candidate/{id}/check-order
  → GetOrderDetailAsync(chadan) → 返回 { brokerTip, brokerRejected, candidateStatus }
  → 若被拒 → 自动重置候选，前端刷新后可重新操作
```

## 编译 & 测试
- 全量编译：0 错误，26 警告（均为此前已有）
- 全量测试：239/239 通过
