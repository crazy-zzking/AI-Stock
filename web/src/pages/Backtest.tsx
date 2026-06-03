import React, { useState } from 'react';
import {
  Card, Row, Col, Statistic, InputNumber, Select, Button, Table, Tag,
  message, Space, DatePicker, Alert,
} from 'antd';
import { ExperimentOutlined, PlayCircleOutlined } from '@ant-design/icons';
import dayjs, { Dayjs } from 'dayjs';
import { getBacktest } from '../api';
import type { BacktestReportDto, BacktestTradeDto } from '../api';
import { pct, upDownColor as upDown } from '../utils/format';

/** 选股回测 — 把历史选股结果按持有期/买点回测，统计胜率/收益/盈亏比/回撤 */
const Backtest: React.FC = () => {
  const [holdDays, setHoldDays] = useState(5);
  const [entry, setEntry] = useState('NextOpen');
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [report, setReport] = useState<BacktestReportDto | null>(null);
  const [loading, setLoading] = useState(false);

  const run = async () => {
    setLoading(true);
    try {
      const from = range?.[0]?.format('YYYY-MM-DD');
      const to = range?.[1]?.format('YYYY-MM-DD');
      const res = await getBacktest(holdDays, entry, from, to);
      setReport(res.data);
      if (res.data.executedTrades === 0) message.warning('无可回测成交（检查是否有历史选股记录与对应 K 线）');
    } catch {
      message.error('回测失败，请确认后端可用');
    } finally {
      setLoading(false);
    }
  };

  const columns = [
    { title: '代码', dataIndex: 'code', key: 'code', width: 90 },
    { title: '名称', dataIndex: 'name', key: 'name', width: 110, ellipsis: true },
    { title: '信号日', dataIndex: 'signalDate', key: 'signalDate', width: 110, render: (v: string) => v?.slice(0, 10) },
    { title: '买入价', dataIndex: 'entryPrice', key: 'entryPrice', width: 90, render: (v: number) => v?.toFixed(2) },
    { title: '卖出价', dataIndex: 'exitPrice', key: 'exitPrice', width: 90, render: (v: number) => v?.toFixed(2) },
    { title: '持有(日)', dataIndex: 'holdDays', key: 'holdDays', width: 80 },
    {
      title: '收益', dataIndex: 'returnPct', key: 'returnPct', width: 90,
      sorter: (a: BacktestTradeDto, b: BacktestTradeDto) => a.returnPct - b.returnPct,
      render: (v: number) => <span style={{ color: upDown(v), fontWeight: 600 }}>{pct(v)}</span>,
    },
    { title: '最高浮盈', dataIndex: 'maxRisePct', key: 'maxRisePct', width: 90, render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
    { title: '最大浮亏', dataIndex: 'maxDropPct', key: 'maxDropPct', width: 90, render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
  ];

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
          <Button type="primary" icon={<PlayCircleOutlined />} loading={loading} onClick={run}>运行回测</Button>
        </Space>
      </Card>

      {report && (
        <>
          <Row gutter={16} style={{ marginBottom: 16 }}>
            <Col span={4}><Card><Statistic title="成交笔数" value={report.executedTrades} suffix={`/${report.totalSignals}`} /></Card></Col>
            <Col span={4}><Card><Statistic title="胜率" value={report.winRatePct} suffix="%" valueStyle={{ color: report.winRatePct >= 50 ? '#cf1322' : '#3f8600' }} /></Card></Col>
            <Col span={4}><Card><Statistic title="平均收益" value={report.avgReturnPct} suffix="%" valueStyle={{ color: upDown(report.avgReturnPct) }} /></Card></Col>
            <Col span={4}><Card><Statistic title="中位收益" value={report.medianReturnPct} suffix="%" valueStyle={{ color: upDown(report.medianReturnPct) }} /></Card></Col>
            <Col span={4}><Card><Statistic title="盈亏比" value={report.profitFactor ?? '—'} /></Card></Col>
            <Col span={4}><Card><Statistic title="最大回撤" value={report.maxDrawdownPct} suffix="pt" valueStyle={{ color: '#3f8600' }} /></Card></Col>
          </Row>
          <Row gutter={16} style={{ marginBottom: 16 }}>
            <Col span={4}><Card size="small"><Statistic title="收益标准差" value={report.stdDevPct} suffix="%" /></Card></Col>
            <Col span={4}><Card size="small"><Statistic title="平均最高浮盈" value={report.avgMaxRisePct} suffix="%" valueStyle={{ color: upDown(report.avgMaxRisePct) }} /></Card></Col>
            <Col span={4}><Card size="small"><Statistic title="平均最大浮亏" value={report.avgMaxDropPct} suffix="%" valueStyle={{ color: upDown(report.avgMaxDropPct) }} /></Card></Col>
            <Col span={4}><Card size="small"><Statistic title="最佳" value={report.bestReturnPct} suffix="%" valueStyle={{ color: upDown(report.bestReturnPct) }} /></Card></Col>
            <Col span={4}><Card size="small"><Statistic title="最差" value={report.worstReturnPct} suffix="%" valueStyle={{ color: upDown(report.worstReturnPct) }} /></Card></Col>
            <Col span={4}><Card size="small"><Statistic title="无数据跳过" value={report.skippedNoData} /></Card></Col>
          </Row>

          <Card title={`成交明细（持有 ${report.holdDays} 日 · ${report.entry === 'SignalClose' ? '信号日收盘买入' : 'T+1 开盘买入'}）`}>
            <Table
              rowKey={(r) => `${r.code}-${r.signalDate}`}
              columns={columns}
              dataSource={report.trades}
              size="small"
              pagination={{ pageSize: 20 }}
              scroll={{ x: 800 }}
            />
          </Card>
        </>
      )}
    </div>
  );
};

export default Backtest;
