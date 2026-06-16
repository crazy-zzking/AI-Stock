import React, { useCallback, useEffect, useState } from 'react';
import {
  Card, Table, Tag, Button, Space, Segmented, message, Modal, InputNumber,
  Tooltip, Typography, Popconfirm, Row, Col,
} from 'antd';
import { ReloadOutlined, ShoppingCartOutlined } from '@ant-design/icons';
import { getTradeCandidates, orderTradeCandidate, ignoreTradeCandidate } from '../api';
import type { TradeCandidate } from '../api';

const { Paragraph, Text } = Typography;

const STATUS_LABEL: Record<number, { text: string; color: string }> = {
  0: { text: '待处理', color: 'blue' },
  1: { text: '已下单', color: 'green' },
  2: { text: '已忽略', color: 'default' },
};

const price = (v?: number) => (typeof v === 'number' ? v.toFixed(2) : '—');

const TradeCandidates: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [items, setItems] = useState<TradeCandidate[]>([]);
  const [mode, setMode] = useState<string>('');
  const [halted, setHalted] = useState(false);
  const [status, setStatus] = useState<number>(0);

  // 下单弹窗
  const [orderOpen, setOrderOpen] = useState(false);
  const [target, setTarget] = useState<TradeCandidate | null>(null);
  const [orderPrice, setOrderPrice] = useState<number>(0);
  const [orderVolume, setOrderVolume] = useState<number>(0);
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async (st = status) => {
    setLoading(true);
    try {
      const res = await getTradeCandidates({ days: 30, status: st });
      setItems(res.data?.items || []);
      setMode(res.data?.mode || '');
      setHalted(!!res.data?.halted);
    } catch {
      message.error('加载候选池失败');
    } finally {
      setLoading(false);
    }
  }, [status]);

  useEffect(() => { load(status); /* eslint-disable-next-line */ }, [status]);

  const openOrder = (c: TradeCandidate) => {
    setTarget(c);
    const p = c.buyHigh > 0 ? c.buyHigh : c.refClose;
    setOrderPrice(p);
    setOrderVolume(p > 0 ? Math.max(100, Math.floor(100000 / p / 100) * 100) : 100);
    setOrderOpen(true);
  };

  const submitOrder = async () => {
    if (!target) return;
    if (!orderVolume || orderVolume % 100 !== 0) { message.warning('数量须为 100 的整数倍'); return; }
    setSubmitting(true);
    try {
      const res = await orderTradeCandidate(target.id, { price: orderPrice, volume: orderVolume });
      if (res.data?.success) {
        message.success(`下单成功（${mode === 'Live' ? '实盘' : '模拟'}）：${res.data.message || ''}`);
        setOrderOpen(false);
        load(status);
      } else {
        message.error(`下单未成交：${res.data?.message || res.data?.status || '未知'}`);
      }
    } catch {
      message.error('下单请求失败');
    } finally {
      setSubmitting(false);
    }
  };

  const doIgnore = async (c: TradeCandidate) => {
    try {
      await ignoreTradeCandidate(c.id);
      message.success('已忽略');
      load(status);
    } catch {
      message.error('操作失败');
    }
  };

  const columns = [
    {
      title: '股票', key: 'name', fixed: 'left' as const, width: 130,
      render: (_: unknown, r: TradeCandidate) => (
        <span>{r.name} <Text type="secondary" style={{ fontSize: 12 }}>{r.code}</Text></span>
      ),
    },
    { title: '策略', dataIndex: 'strategyName', width: 110, render: (v: string) => <Tag color="purple">{v}</Tag> },
    { title: '置信度', dataIndex: 'confidence', width: 80, sorter: (a: TradeCandidate, b: TradeCandidate) => a.confidence - b.confidence, render: (v: number) => `${v}` },
    {
      title: 'AI 推荐理由', dataIndex: 'narrative', width: 280,
      render: (v: string) => (
        <Tooltip title={v}><Paragraph ellipsis={{ rows: 2 }} style={{ marginBottom: 0, maxWidth: 280 }}>{v || '—'}</Paragraph></Tooltip>
      ),
    },
    {
      title: '风险', dataIndex: 'riskFlags', width: 140,
      render: (flags: string[]) => flags?.length
        ? flags.map((f) => <Tag color="orange" key={f}>{f}</Tag>)
        : <Text type="secondary">—</Text>,
    },
    { title: '参考价', dataIndex: 'refClose', width: 75, render: price },
    {
      title: '买入区间', key: 'buy', width: 110,
      render: (_: unknown, r: TradeCandidate) => <Text style={{ color: '#1677ff' }}>{price(r.buyLow)}~{price(r.buyHigh)}</Text>,
    },
    { title: '止损', dataIndex: 'stopLoss', width: 75, render: (v: number) => <Text style={{ color: '#3f8600' }}>{price(v)}</Text> },
    { title: '止盈', dataIndex: 'takeProfit', width: 75, render: (v: number) => <Text style={{ color: '#cf1322' }}>{price(v)}</Text> },
    { title: '盈亏比', dataIndex: 'riskReward', width: 75, render: (v: number) => (v > 0 ? `${v}:1` : '—') },
    {
      title: '操作', key: 'action', fixed: 'right' as const, width: 160,
      render: (_: unknown, r: TradeCandidate) => {
        if (r.status === 1) return <Text type="success">已下单 {r.orderVolume}股@{price(r.orderPrice)}</Text>;
        if (r.status === 2) return <Text type="secondary">已忽略</Text>;
        return (
          <Space>
            <Button type="primary" size="small" icon={<ShoppingCartOutlined />} onClick={() => openOrder(r)}>买入</Button>
            <Popconfirm title="忽略该候选？" onConfirm={() => doIgnore(r)}>
              <Button size="small">忽略</Button>
            </Popconfirm>
          </Space>
        );
      },
    },
  ];

  const pendingCount = items.filter((i) => i.status === 0).length;

  return (
    <div>
      <h2>交易候选池</h2>
      <Card size="small" style={{ marginBottom: 12 }}>
        <Row gutter={16} align="middle">
          <Col flex="auto">
            <Space size="large">
              <span>当前模式：{mode === 'Live' ? <Tag color="red">实盘</Tag> : <Tag color="blue">模拟 DryRun</Tag>}</span>
              {halted && <Tag color="red">熔断中</Tag>}
              <Segmented
                value={status}
                onChange={(v) => setStatus(v as number)}
                options={[
                  { label: '待处理', value: 0 },
                  { label: '已下单', value: 1 },
                  { label: '已忽略', value: 2 },
                  { label: '全部', value: -1 },
                ]}
              />
            </Space>
          </Col>
          <Col>
            <Button icon={<ReloadOutlined />} onClick={() => load(status)} loading={loading}>刷新</Button>
          </Col>
        </Row>
      </Card>

      <Card
        size="small"
        title={`候选池 · 共 ${items.length} 条${status === -1 ? `（待处理 ${pendingCount}）` : ''}`}
      >
        <Table
          rowKey="id"
          size="small"
          loading={loading}
          dataSource={items}
          columns={columns}
          scroll={{ x: 'max-content' }}
          pagination={{ pageSize: 20, showSizeChanger: true }}
          locale={{ emptyText: '暂无候选（每日选股 + LLM 复评后「建议买入」的票会自动入池）' }}
        />
        <div style={{ marginTop: 8, color: '#999', fontSize: 12 }}>
          说明：候选由每日选股经 LLM 复评后自动生成，买入价/止损/止盈由 LLM 给出。下单经交易闸门（{mode === 'Live' ? '实盘' : '模拟下单，不实际发单'}）。
        </div>
      </Card>

      <Modal
        title={target ? `买入 ${target.name}（${target.code}）` : '买入'}
        open={orderOpen}
        onCancel={() => setOrderOpen(false)}
        onOk={submitOrder}
        okText={mode === 'Live' ? '确认下单（实盘）' : '确认下单（模拟）'}
        confirmLoading={submitting}
      >
        {target && (
          <div style={{ lineHeight: 2 }}>
            <div>
              AI 建议：买入 <Text strong style={{ color: '#1677ff' }}>{price(target.buyLow)}~{price(target.buyHigh)}</Text>，
              止损 <Text style={{ color: '#3f8600' }}>{price(target.stopLoss)}</Text>，
              止盈 <Text style={{ color: '#cf1322' }}>{price(target.takeProfit)}</Text>
              {target.riskReward > 0 && <Text type="secondary">（盈亏比 {target.riskReward}:1）</Text>}
            </div>
            <div style={{ color: '#999', fontSize: 12, marginBottom: 12 }}>{target.planBasis}</div>
            <Space size="large">
              <span>下单价：<InputNumber value={orderPrice} min={0} step={0.01} precision={2} onChange={(v) => setOrderPrice(v || 0)} /></span>
              <span>数量：<InputNumber value={orderVolume} min={100} step={100} onChange={(v) => setOrderVolume(v || 0)} /> 股</span>
            </Space>
            <div style={{ marginTop: 8, color: '#999' }}>
              约 ¥{((orderPrice || 0) * (orderVolume || 0)).toLocaleString()} · {mode === 'Live' ? '⚠ 实盘真实下单' : '模拟下单（DryRun，仅记录不实际发单）'}
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
};

export default TradeCandidates;
