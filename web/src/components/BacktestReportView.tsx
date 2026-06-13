import React from 'react';
import { Card, Row, Col, Statistic, Button, Table, Tag, Space, Tooltip } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import type { BacktestReportDto, BacktestTradeDto } from '../api';
import { pct, upDownColor as upDown } from '../utils/format';

/** 把成交明细导出为 CSV（Excel 友好，UTF-8 BOM）。 */
function exportTradesCsv(report: BacktestReportDto, filename: string) {
  const head = ['代码', '名称', '信号日', '买入日', '买入价', '卖出日', '卖出价', '持有日', '出场', '收益%', '最高浮盈%', '最大浮亏%'];
  const reasonText = (r: string) => (r === 'stoploss' ? '止损' : r === 'takeprofit' ? '止盈' : '到期');
  const rows = report.trades.map((t) => [
    t.code, t.name, t.signalDate?.slice(0, 10), t.entryDate?.slice(0, 10), t.entryPrice?.toFixed(2),
    t.exitDate?.slice(0, 10), t.exitPrice?.toFixed(2), t.holdDays,
    reasonText(t.exitReason) + (t.exitDeferred ? '(顺延)' : ''),
    t.returnPct?.toFixed(2), t.maxRisePct?.toFixed(2), t.maxDropPct?.toFixed(2),
  ]);
  const csv = [head, ...rows].map((r) => r.map((c) => `"${c ?? ''}"`).join(',')).join('\n');
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

const tradeColumns = [
  { title: '代码', dataIndex: 'code', key: 'code', width: 80 },
  { title: '名称', dataIndex: 'name', key: 'name', width: 100, ellipsis: true },
  { title: '信号日', dataIndex: 'signalDate', key: 'signalDate', width: 100, render: (v: string) => v?.slice(0, 10) },
  { title: '买入日', dataIndex: 'entryDate', key: 'entryDate', width: 100, render: (v: string) => v?.slice(0, 10) },
  { title: '买入价', dataIndex: 'entryPrice', key: 'entryPrice', width: 80, render: (v: number) => v?.toFixed(2) },
  {
    title: '卖出', key: 'exit', width: 150,
    render: (_: unknown, r: BacktestTradeDto) => (
      <Space size={4}>
        <span>{r.exitDate?.slice(0, 10)}</span>
        <span>@{r.exitPrice?.toFixed(2)}</span>
        {r.exitReason === 'stoploss' && <Tag color="red">止损</Tag>}
        {r.exitReason === 'takeprofit' && <Tag color="green">止盈</Tag>}
        {r.exitDeferred && <Tooltip title="原定卖出日一字跌停，顺延成交"><Tag color="orange">延</Tag></Tooltip>}
      </Space>
    ),
  },
  { title: '持有', dataIndex: 'holdDays', key: 'holdDays', width: 60 },
  {
    title: '收益', dataIndex: 'returnPct', key: 'returnPct', width: 90,
    sorter: (a: BacktestTradeDto, b: BacktestTradeDto) => a.returnPct - b.returnPct,
    render: (v: number) => <span style={{ color: upDown(v), fontWeight: 600 }}>{pct(v)}</span>,
  },
  { title: '最高浮盈', dataIndex: 'maxRisePct', key: 'maxRisePct', width: 90, render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
  { title: '最大浮亏', dataIndex: 'maxDropPct', key: 'maxDropPct', width: 90, render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
];

/** 回测报告展示：核心指标卡 + 成交明细交割单（带 CSV 导出）。选股回测与回放回测共用。 */
const BacktestReportView: React.FC<{ report: BacktestReportDto; csvName?: string }> = ({ report, csvName = 'backtest' }) => {
  const sl = report.trades.filter((t) => t.exitReason === 'stoploss').length;
  const tp = report.trades.filter((t) => t.exitReason === 'takeprofit').length;
  const entryText = report.entry === 'SignalClose' ? '信号日收盘买入' : 'T+1 开盘买入';

  return (
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
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={4}>
          <Card size="small">
            <Tooltip title="买入日开盘即≈涨停（一字板），实盘买不进，剔除不计收益">
              <Statistic title="一字板剔除" value={report.skippedUntradable} valueStyle={{ color: report.skippedUntradable > 0 ? '#fa8c16' : undefined }} />
            </Tooltip>
          </Card>
        </Col>
        <Col span={4}><Card size="small"><Tooltip title="卖出日一字跌停卖不出，顺延到下一个可卖日成交"><Statistic title="跌停顺延" value={report.deferredExits} /></Tooltip></Card></Col>
        <Col span={4}><Card size="small"><Statistic title="已扣摩擦" value={report.frictionPct} suffix="%/笔" /></Card></Col>
        {(sl > 0 || tp > 0) && (
          <Col span={4}><Card size="small"><Statistic title="止损/止盈出场" value={`${sl}/${tp}`} /></Card></Col>
        )}
      </Row>

      <Card
        title={`成交明细（持有 ${report.holdDays} 日 · ${entryText}）`}
        extra={(
          <Button
            icon={<DownloadOutlined />}
            disabled={report.trades.length === 0}
            onClick={() => exportTradesCsv(report, `${csvName}_${new Date().toISOString().slice(0, 10)}.csv`)}
          >
            导出 CSV
          </Button>
        )}
      >
        <Table
          rowKey={(r) => `${r.code}-${r.signalDate}-${r.entryDate}`}
          columns={tradeColumns}
          dataSource={report.trades}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: true }}
          scroll={{ x: 1000 }}
        />
      </Card>
    </>
  );
};

export default BacktestReportView;
