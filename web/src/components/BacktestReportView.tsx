import React from 'react';
import { Card, Row, Col, Statistic, Button, Table, Tag, Space, Tooltip, Alert } from 'antd';
import { DownloadOutlined, WarningOutlined } from '@ant-design/icons';
import type { BacktestReportDto, BacktestTradeDto, EquityPointDto } from '../api';
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

/** 简易 SVG 权益曲线图 */
const EquityCurveChart: React.FC<{ data: EquityPointDto[] }> = ({ data }) => {
  if (!data || data.length < 2) return null;
  const w = 680, h = 200, pad = { top: 20, right: 20, bottom: 30, left: 50 };
  const vals = data.map((d) => d.nav);
  const minV = Math.min(...vals) * 0.99;
  const maxV = Math.max(...vals) * 1.01;
  const xScale = (i: number) => pad.left + (i / (data.length - 1)) * (w - pad.left - pad.right);
  const yScale = (v: number) => h - pad.bottom - ((v - minV) / (maxV - minV)) * (h - pad.top - pad.bottom);
  const points = vals.map((v, i) => `${xScale(i).toFixed(1)},${yScale(v).toFixed(1)}`).join(' ');
  // 基准曲线
  const benchVals = data.map((d) => d.benchmarkNav ?? d.benchmarkNav);
  const hasBench = data.some((d) => d.benchmarkNav != null);
  let benchPoints = '';
  if (hasBench) {
    const bMin = Math.min(...data.map((d) => d.benchmarkNav!)) * 0.99;
    const bMax = Math.max(...data.map((d) => d.benchmarkNav!)) * 1.01;
    const bY = (v: number) => h - pad.bottom - ((v - bMin) / (bMax - bMin)) * (h - pad.top - pad.bottom);
    benchPoints = data.map((d, i) => d.benchmarkNav != null ? `${xScale(i).toFixed(1)},${bY(d.benchmarkNav).toFixed(1)}` : '').filter(Boolean).join(' ');
  }
  // X 轴日期标签（每 10% 取一个）
  const tickIndices = [0, ...Array.from({ length: 4 }, (_, i) => Math.floor((i + 1) * data.length / 5)), data.length - 1];
  return (
    <svg viewBox={`0 0 ${w} ${h}`} style={{ width: '100%', maxWidth: w, background: '#fafafa', borderRadius: 4 }}>
      {/* 网格线 */}
      {[0, 0.25, 0.5, 0.75, 1].map((r) => {
        const y = pad.top + r * (h - pad.top - pad.bottom);
        return <line key={r} x1={pad.left} y1={y} x2={w - pad.right} y2={y} stroke="#eee" strokeWidth={1} />;
      })}
      {/* 策略曲线 */}
      <polyline points={points} fill="none" stroke="#cf1322" strokeWidth={2} />
      {/* 基准曲线 */}
      {benchPoints && <polyline points={benchPoints} fill="none" stroke="#666" strokeWidth={1.5} strokeDasharray="4 2" />}
      {/* 日期标签 */}
      {tickIndices.map((i) => (
        <text key={i} x={xScale(i)} y={h - 8} fontSize={9} textAnchor="middle" fill="#999">
          {data[i].date.slice(5)}
        </text>
      ))}
      {/* 图例 */}
      <rect x={pad.left} y={6} width={12} height={2} fill="#cf1322" />
      <text x={pad.left + 16} y={10} fontSize={10} fill="#666">策略</text>
      {hasBench && (
        <>
          <rect x={pad.left + 60} y={6} width={12} height={2} fill="#666" />
          <text x={pad.left + 76} y={10} fontSize={10} fill="#666">基准</text>
        </>
      )}
    </svg>
  );
};

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

/** 回测报告展示：核心指标卡 + 权益曲线 + 成交明细交割单（带 CSV 导出）。选股回测与回放回测共用。 */
const BacktestReportView: React.FC<{ report: BacktestReportDto; csvName?: string }> = ({ report, csvName = 'backtest' }) => {
  const sl = report.trades.filter((t) => t.exitReason === 'stoploss').length;
  const tp = report.trades.filter((t) => t.exitReason === 'takeprofit').length;
  const entryText = report.entry === 'SignalClose' ? '信号日收盘买入' : 'T+1 开盘买入';

  return (
    <>
      {/* 数据质量告警 */}
      {report.warnings && report.warnings.length > 0 && (
        <Alert
          type="warning"
          icon={<WarningOutlined />}
          style={{ marginBottom: 16 }}
          message="数据质量告警"
          description={
            <ul style={{ margin: 0, paddingLeft: 20 }}>
              {report.warnings.map((w, i) => <li key={i} style={{ fontSize: 13 }}>{w}</li>)}
            </ul>
          }
        />
      )}

      {/* 统计显著性告警 */}
      {report.sampleSize !== undefined && report.sampleSize < 20 && (
        <Alert type="warning" style={{ marginBottom: 16 }} message={`样本量较小（${report.sampleSize} 笔），统计结果仅供参考`} />
      )}

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

      {/* Phase 4-6 新指标 */}
      {(report.sharpeRatio !== undefined || report.alpha !== undefined || report.pValue !== undefined) && (
        <Row gutter={16} style={{ marginBottom: 16 }}>
          {report.sharpeRatio !== undefined && (
            <Col span={4}><Card size="small"><Statistic title="Sharpe" value={report.sharpeRatio} precision={2} valueStyle={{ color: report.sharpeRatio > 1 ? '#cf1322' : undefined }} /></Card></Col>
          )}
          {report.alpha !== undefined && (
            <Col span={4}><Card size="small"><Statistic title="Alpha" value={report.alpha} suffix="%" valueStyle={{ color: upDown(report.alpha) }} /></Card></Col>
          )}
          {report.beta !== undefined && (
            <Col span={4}><Card size="small"><Statistic title="Beta" value={report.beta} precision={2} /></Card></Col>
          )}
          {report.informationRatio !== undefined && (
            <Col span={4}><Card size="small"><Statistic title="IR" value={report.informationRatio} precision={2} /></Card></Col>
          )}
          {report.benchmarkReturn !== undefined && (
            <Col span={4}><Card size="small"><Statistic title="基准收益" value={report.benchmarkReturn} suffix="%" valueStyle={{ color: upDown(report.benchmarkReturn) }} /></Card></Col>
          )}
          {report.pValue !== undefined && (
            <Col span={4}><Card size="small">
              <Statistic title="p 值" value={report.pValue} precision={3} valueStyle={{ color: report.pValue < 0.05 ? '#cf1322' : '#3f8600' }} />
              {report.confidenceInterval && <div style={{ fontSize: 11, color: '#999' }}>95% CI [{report.confidenceInterval[0].toFixed(1)}, {report.confidenceInterval[1].toFixed(1)}]</div>}
            </Card></Col>
          )}
        </Row>
      )}

      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={4}>
          <Card size="small">
            <Tooltip title="买入日开盘即≈涨停（一字板），实盘买不进，剔除不计收益">
              <Statistic title="一字板剔除" value={report.skippedUntradable} valueStyle={{ color: report.skippedUntradable > 0 ? '#fa8c16' : undefined }} />
            </Tooltip>
          </Card>
        </Col>
        <Col span={4}><Card size="small"><Tooltip title="卖出日一字跌停卖不出，顺延到下一个可卖日成交"><Statistic title="跌停顺延" value={report.deferredExits} /></Tooltip></Card></Col>
        {(report.skippedBreakdown ?? 0) > 0 && (
          <Col span={4}>
            <Card size="small">
              <Tooltip title="信号日收盘跌破MA10 / MA5拐头向下斜率过大，破位否决不买入">
                <Statistic title="破位否决" value={report.skippedBreakdown} valueStyle={{ color: '#fa8c16' }} />
              </Tooltip>
            </Card>
          </Col>
        )}
        <Col span={4}><Card size="small"><Statistic title="已扣摩擦" value={report.frictionPct} suffix="%/笔" /></Card></Col>
        {(sl > 0 || tp > 0) && (
          <Col span={4}><Card size="small"><Statistic title="止损/止盈出场" value={`${sl}/${tp}`} /></Card></Col>
        )}
      </Row>

      {/* 权益曲线图 */}
      {report.equityCurve && report.equityCurve.length >= 2 && (
        <Card title="权益曲线" style={{ marginBottom: 16 }}>
          <EquityCurveChart data={report.equityCurve} />
        </Card>
      )}

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
