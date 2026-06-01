import React, { useEffect, useState } from 'react';
import {
  Card, Row, Col, Statistic, Tag, Table, Spin, message, Button, Alert, Typography, Space, List,
} from 'antd';
import { ReloadOutlined, ThunderboltOutlined, CheckCircleTwoTone, CloseCircleTwoTone } from '@ant-design/icons';
import { getLatestReview, runReview } from '../api';

const { Paragraph, Text } = Typography;

const pct = (v?: number) => (v == null ? '—' : `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`);
const upDown = (v?: number) => ((v ?? 0) >= 0 ? '#cf1322' : '#3f8600');
const yi = (v?: number) => {
  const x = v ?? 0; const abs = Math.abs(x);
  if (abs >= 1e8) return `${(x / 1e8).toFixed(2)}亿`;
  if (abs >= 1e4) return `${(x / 1e4).toFixed(0)}万`;
  return `${x.toFixed(0)}`;
};

interface DataGap { source: string; available: boolean; note: string; }
interface SectorItem { sectorName: string; changePercent: number; netInflow: number; leadingStocks: string[]; reason: string; }
interface StockItem {
  code: string; name: string; changePercent: number; mainNetInflow: number; turnoverRate: number;
  isLimitUp: boolean; concepts: string[]; hotConcepts: string[]; onDragonTiger: boolean; events: string[]; reason: string;
}
interface ThemeItem { concept: string; activeStockCount: number; leadingStocks: string[]; }
interface SelItem { code: string; name: string; changePercent?: number; hit: boolean; }
interface Review {
  tradingDate: string; generatedAt: string; summary: string;
  orders: { totalOrders: number; successOrders: number; failedOrders: number; buyOrders: number; sellOrders: number; totalValue: number; gateMode: string; };
  market: { totalCount: number; upCount: number; downCount: number; limitUpCount: number; totalMainNetInflow: number; avgChangePercent: number; };
  topSectors: SectorItem[]; topStocks: StockItem[]; hotThemes: ThemeItem[];
  selection?: { selectionRunAt: string; count: number; hitCount: number; avgChangePercent: number; items: SelItem[]; };
  dataGaps: DataGap[];
}

const Review: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [r, setR] = useState<Review | null>(null);

  const load = async () => {
    setLoading(true);
    try {
      const res = await getLatestReview();
      setR(res.data || null);
    } catch {
      message.error('加载复盘失败，请确认后端 /api/review 可用');
    } finally {
      setLoading(false);
    }
  };

  const rerun = async () => {
    setRunning(true);
    try {
      const res = await runReview();
      setR(res.data || null);
      message.success('已重新生成当日复盘');
    } catch {
      message.error('生成复盘失败');
    } finally {
      setRunning(false);
    }
  };

  useEffect(() => { load(); }, []);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  if (!r || !r.tradingDate) {
    return (
      <div>
        <h2>每日复盘</h2>
        <Alert type="info" showIcon message="暂无复盘数据" description="请先跑 market-snapshot / dragon-tiger 采集生成当日快照，再点「重新生成」。" />
        <Button style={{ marginTop: 12 }} icon={<ThunderboltOutlined />} loading={running} onClick={rerun}>重新生成</Button>
      </div>
    );
  }

  const m = r.market;
  return (
    <div>
      <h2>
        每日复盘 · {r.tradingDate?.slice(0, 10)}
        <Space style={{ marginLeft: 16 }}>
          <Button icon={<ReloadOutlined />} size="small" onClick={load}>刷新</Button>
          <Button type="primary" size="small" icon={<ThunderboltOutlined />} loading={running} onClick={rerun}>重新生成</Button>
        </Space>
      </h2>

      <Alert type="info" showIcon style={{ marginBottom: 16 }} message="复盘总结"
        description={<Paragraph style={{ marginBottom: 0 }}>{r.summary}</Paragraph>} />

      {/* 市场宽度 + 交易统计 */}
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={4}><Card size="small"><Statistic title="上涨家数" value={m.upCount} valueStyle={{ color: '#cf1322' }} /></Card></Col>
        <Col span={4}><Card size="small"><Statistic title="下跌家数" value={m.downCount} valueStyle={{ color: '#3f8600' }} /></Card></Col>
        <Col span={4}><Card size="small"><Statistic title="涨停" value={m.limitUpCount} valueStyle={{ color: '#cf1322' }} /></Card></Col>
        <Col span={4}><Card size="small"><Statistic title="平均涨幅" value={pct(m.avgChangePercent)} valueStyle={{ color: upDown(m.avgChangePercent) }} /></Card></Col>
        <Col span={4}><Card size="small"><Statistic title="主力净额" value={yi(m.totalMainNetInflow)} valueStyle={{ color: upDown(m.totalMainNetInflow) }} /></Card></Col>
        <Col span={4}><Card size="small"><Statistic title="当日下单" value={r.orders.totalOrders} suffix={`/ ${r.orders.gateMode}`} /></Card></Col>
      </Row>

      <Row gutter={16}>
        <Col span={14}>
          {/* 领涨个股 + 涨因 */}
          <Card title="领涨个股 · 为什么涨" size="small" style={{ marginBottom: 16 }}>
            <Table
              size="small" pagination={false} rowKey="code" dataSource={r.topStocks}
              columns={[
                { title: '名称', key: 'name', render: (_: unknown, s: StockItem) => (
                  <span>{s.name} <Text type="secondary" style={{ fontSize: 12 }}>{s.code}</Text>{s.isLimitUp && <Tag color="red" style={{ marginLeft: 4 }}>涨停</Tag>}</span>
                ) },
                { title: '涨幅', dataIndex: 'changePercent', width: 80, render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
                { title: '主力', dataIndex: 'mainNetInflow', width: 90, render: (v: number) => <span style={{ color: upDown(v) }}>{yi(v)}</span> },
                { title: '涨因', dataIndex: 'reason' },
              ]}
            />
          </Card>

          {/* 领涨板块 */}
          <Card title="领涨板块" size="small">
            <Table
              size="small" pagination={false} rowKey="sectorName" dataSource={r.topSectors}
              locale={{ emptyText: '板块数据不可用（东财接口/隧道代理）' }}
              columns={[
                { title: '板块', dataIndex: 'sectorName' },
                { title: '涨幅', dataIndex: 'changePercent', width: 80, render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
                { title: '主力净额', dataIndex: 'netInflow', width: 90, render: (v: number) => <span style={{ color: upDown(v) }}>{yi(v)}</span> },
                { title: '领涨股', dataIndex: 'leadingStocks', render: (v: string[]) => (v || []).join('、') },
              ]}
            />
          </Card>
        </Col>

        <Col span={10}>
          {/* 热门题材 */}
          <Card title="当下风口题材（活跃股扎堆）" size="small" style={{ marginBottom: 16 }}>
            {r.hotThemes.length === 0 ? <Text type="secondary">无</Text> : (
              <Space size={[8, 8]} wrap>
                {r.hotThemes.map((t) => (
                  <Tag color="volcano" key={t.concept}>{t.concept} · {t.activeStockCount}只</Tag>
                ))}
              </Space>
            )}
          </Card>

          {/* 选股回测 */}
          {r.selection && (
            <Card title={`选股回测 · ${r.selection.count}选${r.selection.hitCount}红 · 均${pct(r.selection.avgChangePercent)}`} size="small" style={{ marginBottom: 16 }}>
              <List size="small" dataSource={r.selection.items}
                renderItem={(it) => (
                  <List.Item>
                    {it.hit ? <CheckCircleTwoTone twoToneColor="#cf1322" /> : <CloseCircleTwoTone twoToneColor="#999" />}
                    <span style={{ marginLeft: 8 }}>{it.name} <Text type="secondary" style={{ fontSize: 12 }}>{it.code}</Text></span>
                    <span style={{ marginLeft: 'auto', color: upDown(it.changePercent) }}>{pct(it.changePercent)}</span>
                  </List.Item>
                )} />
            </Card>
          )}

          {/* 数据完备性 */}
          <Card title="数据完备性诊断" size="small">
            <List size="small" dataSource={r.dataGaps}
              renderItem={(g) => (
                <List.Item>
                  {g.available ? <CheckCircleTwoTone twoToneColor="#52c41a" /> : <CloseCircleTwoTone twoToneColor="#faad14" />}
                  <span style={{ marginLeft: 8 }}>{g.source}</span>
                  <Text type="secondary" style={{ marginLeft: 'auto', fontSize: 12, textAlign: 'right', maxWidth: 220 }}>{g.note}</Text>
                </List.Item>
              )} />
          </Card>
        </Col>
      </Row>
    </div>
  );
};

export default Review;
