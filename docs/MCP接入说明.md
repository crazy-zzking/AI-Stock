# 策略优化 MCP 接口接入说明

`AIStock.Mcp` 是一个自包含的 .NET MCP Server（stdio），把"选股参数 / 回放回测 / 配置存取"封装成工具，
供支持 MCP 的大模型客户端（Claude Desktop 等）调用，让大模型**自主优化选股策略**：
读参数 → 用候选参数回放回测 → 对比胜率/盈亏比/回撤 → 保存并激活最优版本。

## 一、构建

```bash
cd AIStock.Mcp
dotnet build -c Release        # 产物：bin/Release/net9.0/AIStock.Mcp.exe
# 或发布为自包含单文件：
# dotnet publish -c Release -r win-x64 --self-contained
```

## 二、在 Claude Desktop 接入

编辑 `%APPDATA%\Claude\claude_desktop_config.json`：

```json
{
  "mcpServers": {
    "aistock-selection": {
      "command": "D:\\代码\\repos\\AI-Stock\\AIStock.Mcp\\bin\\Release\\net9.0\\AIStock.Mcp.exe",
      "env": {
        "ConnectionStrings__MySQL": "Server=你的RDS地址;Port=3306;Database=aistock;User=用户;Password=密码;CharSet=utf8mb4;"
      }
    }
  }
}
```

- **连接串通过 `env` 注入**（不要写进 `appsettings.json` 后提交，避免泄密）。
- 数据库与 `AIStock.Web` 用同一个；回放回测读 `daily_market_snapshot` / `kline_data` / `stock_*`，写 `selection_config`。
- 重启 Claude Desktop 后，对话中即可让模型调用这些工具。

## 三、工具清单

| 工具 | 作用 |
|---|---|
| `list_strategies` | 列出可用策略（lowdip/trend/theme）及适用环境 |
| `get_default_config` | 代码内置默认参数（调参起点，含阈值+8因子权重） |
| `get_active_config` | 某策略当前生效参数 |
| `replay_backtest` | **核心**：用给定参数在历史区间逐日重跑选股并回测，返回胜率/盈亏比/最大回撤等 |
| `backtest_history` | 回测已落库的真实历史选股结果（看现状基线） |
| `save_config` | 保存参数为新版本，`activate=true` 立即生效 |
| `list_configs` | 列出所有配置版本 |

## 四、建议的优化工作流（提示词示例）

> "请优化 lowdip 策略：先用 get_active_config 取当前参数和 replay_backtest 得到基线（持有5日，最近30天）；
> 然后围绕权重(资金/位置/技术…)与阈值(MaxRsi/MaxRise20d/MinMainNetInflow…)做多组候选，逐一 replay_backtest 对比，
> 目标是**提高盈亏比与胜率、同时压低最大回撤**；找到明显更优的一组后用 save_config 存为新版本并激活，最后说明改了什么、回测对比如何。"

模型会自主循环：调参 → `replay_backtest` → 看指标 → 再调 → … → `save_config(activate=true)`。

## 五、注意

- **回放回测的大盘环境用历史指数**：需 Worker 的 `index-kline` 任务先把指数历史日K落库（启动即回补）；
  回放按"截至当日"的历史指数构造环境，与实盘共用 `RegimeEvaluator`、口径一致且无前视。指数历史未采集时自动降级为仅广度判断。
- **回测不计交易成本/滑点/涨跌停不可成交**；等权独立成交，最大回撤为收益序列近似。
- `save_config(activate=true)` 会**直接改变实际选股使用的参数**（已按你的要求开放给模型）；如需回退，用 `list_configs` 找旧版本，激活逻辑同 `/api/selection/config/{id}/activate` 或前端选股配置页。
- 历史回放深度受 `daily_market_snapshot` 已采集的天数限制；天数越多，回测越可信。
