import React, { useState } from 'react';
import {
  Card, InputNumber, Select, Button, message, Space, DatePicker, Alert, Switch, Tooltip,
} from 'antd';
import { ExperimentOutlined, PlayCircleOutlined } from '@ant-design/icons';
import { Dayjs } from 'dayjs';
import { getBacktest } from '../api';
import type { BacktestReportDto } from '../api';
import BacktestReportView from '../components/BacktestReportView';

/** 选股回测 — 把历史选股结果按持有期/买点回测，统计胜率/收益/盈亏比/回撤 */
const Backtest: React.FC = () => {
  const [holdDays, setHoldDays] = useState(5);
  const [entry, setEntry] = useState('NextOpen');
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [tradability, setTradability] = useState(true);
  const [friction, setFriction] = useState(0.3);
  const [stopLoss, setStopLoss] = useState(0);
  const [takeProfit, setTakeProfit] = useState(0);
  const [report, setReport] = useState<BacktestReportDto | null>(null);
  const [loading, setLoading] = useState(false);

  const run = async () => {
    setLoading(true);
    try {
      const from = range?.[0]?.format('YYYY-MM-DD');
      const to = range?.[1]?.format('YYYY-MM-DD');
      const res = await getBacktest(holdDays, entry, from, to, tradability, friction, stopLoss, takeProfit);
      setReport(res.data);
      if (res.data.executedTrades === 0) message.warning('无可回测成交（检查是否有历史选股记录与对应 K 线）');
    } catch {
      message.error('回测失败，请确认后端可用');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2><ExperimentOutlined /> 选股回测</h2>
      <Alert
        type="info"
        style={{ marginBottom: 16 }}
        message="基于已落库的历史选股结果（selection_result）：把每次选出的标的当作信号，按 T+1 开盘（或信号日收盘）买入、持有 N 个交易日卖出，用日 K 统计真实表现。需先有历史选股记录与对应 K 线数据。"
      />

      <Card style={{ marginBottom: 16 }}>
        <Space wrap>
          <span>持有交易日</span>
          <InputNumber min={1} max={60} value={holdDays} onChange={(v) => setHoldDays(v || 5)} style={{ width: 90 }} />
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
          <DatePicker.RangePicker
            value={range as any}
            onChange={(v) => setRange(v as any)}
            allowEmpty={[true, true]}
          />
          <Tooltip title="开启：一字涨停买不进剔除、一字跌停卖出顺延（贴近实盘）；关闭：理想化任意成交">
            <span>可成交性</span>
          </Tooltip>
          <Switch checked={tradability} onChange={setTradability} />
          <Tooltip title="单笔往返摩擦（佣金+印花税+滑点），从每笔收益中扣除">
            <span>摩擦%</span>
          </Tooltip>
          <InputNumber min={0} max={3} step={0.1} value={friction} onChange={(v) => setFriction(v ?? 0.3)} style={{ width: 80 }} />
          <Tooltip title="持有期内最低价跌破 买入价×(1-x%) 即止损卖出（跳空按开盘价更差成交，一字跌停顺延）。0=关闭">
            <span>止损%</span>
          </Tooltip>
          <InputNumber min={0} max={30} step={1} value={stopLoss} onChange={(v) => setStopLoss(v ?? 0)} style={{ width: 80 }} />
          <Tooltip title="持有期内最高价触及 买入价×(1+x%) 即止盈卖出。0=关闭">
            <span>止盈%</span>
          </Tooltip>
          <InputNumber min={0} max={50} step={1} value={takeProfit} onChange={(v) => setTakeProfit(v ?? 0)} style={{ width: 80 }} />
          <Button type="primary" icon={<PlayCircleOutlined />} loading={loading} onClick={run}>运行回测</Button>
        </Space>
      </Card>

      {report && <BacktestReportView report={report} csvName="选股回测" />}
    </div>
  );
};

export default Backtest;
