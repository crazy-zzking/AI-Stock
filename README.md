# AI-Stock

AI自主交易系统（AI Autonomous Trading OS）

## 技术栈

- **后端:** .NET 9
- **前端:** React + Ant Design Pro（待开发）
- **数据库:** MySQL 8.0
- **缓存:** Redis 6.2.6
- **消息总线:** Redis Stream
- **数据源:** 东方财富 / 腾讯财经 / 散户量化

## 快速开始

### 1. 配置

编辑 `AIStock.Web/appsettings.user.json`：

```json
{
  "ConnectionStrings": {
    "MySQL": "Server=localhost;Port=3306;Database=aistock;User=root;Password=xxx;",
    "Redis": "localhost:6379,password=xxx"
  },
  "DataProviders": {
    "Sanhu": {
      "Token": "your_token"
    }
  }
}
```

### 2. 初始化数据库

```bash
mysql -u root -p < scripts/database/init.sql
```

### 3. 启动

```bash
cd AIStock.Web
dotnet run
```

### 4. 验证

```bash
curl http://localhost:5172/api/health
```

## API接口

| 接口 | 说明 |
|------|------|
| `GET /api/health` | 健康检查 |
| `GET /api/stock/list` | 股票列表 |
| `GET /api/stock/{code}/quote` | 实时行情 |
| `GET /api/stock/{code}/kline` | K线数据 |
| `GET /api/stock/{code}/intraday` | 分时数据 |
| `GET /api/stock/providers/status` | 数据源状态 |

## 开发计划

详见 [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md)

## 项目结构

```
AI-Stock/
├── AIStock.Core/           # 核心接口和模型
├── AIStock.Infrastructure/ # 基础设施（数据库/缓存/消息总线）
├── AIStock.Data/           # 数据源Provider
├── AIStock.Web/            # Web API
├── AIStock.Worker/         # 后台服务
└── scripts/database/       # 数据库脚本
```
