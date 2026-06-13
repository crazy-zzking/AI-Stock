import React, { useEffect, useState } from 'react';
import {
  Card, InputNumber, Select, Button, message, Space, DatePicker, Alert, Switch, Tooltip,
} from 'antd';
import { ThunderboltOutlined, PlayCircleOutlined } from '@ant-design/icons';
import dayjs, { Dayjs } from 'dayjs';
import { replayBacktest, getStrategies } from '../api';
import type { BacktestReportDto, StrategyInfo } from '../api';
import BacktestReportView from '../components/BacktestReportView';

/**
 * 回放回测 — 用「当前代码 + 策略生效配置」在历史快照上逐日重跑选股并回测。
 * 区别于「选股回测」（回测已落库的历史选股记录）：这里是"如果该策略当时在跑会怎样"，用于调参验证。
 */
const ReplayBacktest: React.FC = () => {
  const [strategies, setStrategies] = useState<StrategyInfo[]>([]);
  const [strategy, setStrategy] = useState<string>('hotmoney');
  const [holdDays, setHoldDays] = useState(3);
  const [entry, setEntry] = useState('NextOpen');
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(
    [dayjs().add(-90, 'day'), dayjs()],
  );
  const [tradability, setTradability] = useState(true);
  const [friction, setFriction] = useState(0.3);
  const [stopLoss, setStopLoss] = useState(0);
  const [takeProfit, setTakeProfit] = useState(0);
  const [report, setReport] = useState<BacktestReportDto | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    getStrategies().then((r) => setStrategies(r.data)).catch(() => {});
  }, []);

  const run = async () => {
    setLoading(true);
    try {
      const res = await replayBacktest({
        strategy,
        from: range?.[0]?.format('YYYY-MM-DD'),
        to: range?.[1]?.format('YYYY-MM-DD'),
        holdDays, entry, tradability, friction, stopLoss, takeProfit,
      });
      setReport(res.data);
      if (res.data.totalSignals === 0) message.warning('区间内无选股信号（检查快照/指数数据是否覆盖该区间）');
    } catch {
      message.error('回放回测失败，请确认后端可用');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2><ThunderboltOutlined /> 回放回测</h2>
      <Alert
        type="info"
        style={{ marginBottom: 16 }}
        message="用当前代码 + 策略生效配置，在历史快照上逐日重跑选股并回测——回答「这个策略/参数当时在跑会怎样」（调参验证）。改了策略或配置后用它检验历史表现；与「选股回测」（回测已真实落库的选股记录）口径不同。"
      />

      <Card style={{ marginBottom: 16 }}>
        <Space wrap>
          <span>策略</span>
          <Select
            value={strategy}
            onChange={setStrategy}
            style={{ width: 160 }}
            options={strategies.map((s) => ({ value: s.key, label: s.name }))}
          />
          <span>持有交易日</span>
          <InputNumber min={1} max={60} value={holdDays} onChange={(v) => setHoldDays(v || 3)} style={{ width: 80 }} />
          <span>买入时点</span>
          <Select
            value={entry}
            onChange={setEntry}
            style={{ width: 170 }}
            options={[
              { value: 'NextOpen', label: 'T+1 开盘（贴近实盘）' },
              { value: 'SignalClose', label: '信号日收盘（理想化）' },
            ]}
          />
          <span>区间</span>
          <DatePicker.RangePicker value={range as any} onChange={(v) => setRange(v as any)} allowEmpty={[true, true]} />
          <Tooltip title="开启：一字涨停买不进剔除、一字跌停卖出顺延（贴近实盘）">
            <span>可成交性</span>
          </Tooltip>
          <Switch checked={tradability} onChange={setTradability} />
          <Tooltip title="单笔往返摩擦（佣金+印花税+滑点）">
            <span>摩擦%</span>
          </Tooltip>
          <InputNumber min={0} max={3} step={0.1} value={friction} onChange={(v) => setFriction(v ?? 0.3)} style={{ width: 80 }} />
          <Tooltip title="持有期内跌破买入价×(1-x%)止损卖出。0=关闭">
            <span>止损%</span>
          </Tooltip>
          <InputNumber min={0} max={30} step={1} value={stopLoss} onChange={(v) => setStopLoss(v ?? 0)} style={{ width: 80 }} />
          <Tooltip title="持有期内触及买入价×(1+x%)止盈卖出。0=关闭">
            <span>止盈%</span>
          </Tooltip>
          <InputNumber min={0} max={50} step={1} value={takeProfit} onChange={(v) => setTakeProfit(v ?? 0)} style={{ width: 80 }} />
          <Button type="primary" icon={<PlayCircleOutlined />} loading={loading} onClick={run}>运行回放</Button>
        </Space>
      </Card>

      {report && <BacktestReportView report={report} csvName={`回放_${strategy}`} />}
    </div>
  );
};

export default ReplayBacktest;
