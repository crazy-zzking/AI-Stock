import React, { useEffect, useState } from 'react';
import {
  Card, Rate, Tag, Row, Col, Alert, Spin, message, Button,
  InputNumber, Space, Typography, Tooltip,
} from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { getLatestSelection } from '../api';

interface FactorScores {
  capital: number; technical: number; position: number;
  dragonTiger: number; activity: number; form: number;
}
interface SelectionResult {
  code: string; name: string; industry: string; concepts: string[]; hotConcepts: string[];
  ratingStars: number; tags: string[];
  close: number; changePercent: number; totalMarketCap: number;
  rise20d: number; peTtm: number; mainNetInflow: number;
  totalScore: number; factors: FactorScores; coreLogic: string;
}

const pct = (v: number) => `${v >= 0 ? '+' : ''}${(v ?? 0).toFixed(2)}%`;
const upDown = (v: number) => (v >= 0 ? '#cf1322' : '#3f8600'); // A股习惯：红涨绿跌
const yi = (v: number) => (v > 0 ? `${(v / 1e8).toFixed(1)}亿` : '—');

const DataItem: React.FC<{ label: string; value: React.ReactNode; color?: string }> = ({ label, value, color }) => (
  <Col flex="1">
    <div style={{ textAlign: 'center' }}>
      <div style={{ fontSize: 12, color: '#999' }}>{label}</div>
      <div style={{ fontSize: 16, fontWeight: 600, color }}>{value}</div>
    </div>
  </Col>
);

const Selection: React.FC = () => {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [topN, setTopN] = useState(5);
  const [list, setList] = useState<SelectionResult[]>([]);

  const load = async (n: number) => {
    setLoading(true);
    try {
      const res = await getLatestSelection(n);
      setList(res.data || []);
    } catch {
      message.error('加载选股失败，请确认后端 /api/selection 可用');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(topN); /* eslint-disable-next-line */ }, []);

  return (
    <div>
      <h2 style={{ marginBottom: 12 }}>
        明日可介入 — 短线弹性品种 TOP{topN}
        <Space style={{ marginLeft: 16 }}>
          <InputNumber min={1} max={20} value={topN} onChange={(v) => setTopN(v || 5)} size="small" style={{ width: 70 }} />
          <Button icon={<ReloadOutlined />} size="small" onClick={() => load(topN)}>刷新</Button>
        </Space>
      </h2>

      <Alert
        type="warning"
        showIcon
        style={{ marginBottom: 16 }}
        message="筛选逻辑（埋伏型）：温和放量未涨停（规避追高/次日高开）+ 主力净流入为正 + 技术面多头未超买 + 20日涨幅<50%（低位）+ 龙虎榜阵容优质"
      />

      {loading ? (
        <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />
      ) : list.length === 0 ? (
        <Alert
          type="info"
          showIcon
          message="暂无选股结果"
          description="请先运行 Worker 的 market-snapshot / dragon-tiger 采集任务生成当日快照数据，再刷新。"
        />
      ) : (
        list.map((r, i) => (
          <Card
            key={r.code}
            style={{ marginBottom: 16, borderColor: '#52c41a', borderWidth: i === 0 ? 2 : 1 }}
            styles={{ body: { padding: 16 } }}
          >
            <Row justify="space-between" align="middle" style={{ marginBottom: 8 }}>
              <Col>
                <span style={{ fontSize: 18, fontWeight: 700, marginRight: 8 }}>{r.name}</span>
                <Typography.Text type="secondary" style={{ marginRight: 8 }}>{r.code}</Typography.Text>
                {r.industry && <Tag color="geekblue" style={{ marginRight: 8 }}>{r.industry}</Tag>}
                <Rate disabled value={r.ratingStars} style={{ fontSize: 14 }} />
              </Col>
              <Col style={{ textAlign: 'right' }}>
                <Tooltip title={`资金${r.factors.capital}/技术${r.factors.technical}/位置${r.factors.position}/形态${r.factors.form}/龙虎${r.factors.dragonTiger}/活跃${r.factors.activity}`}>
                  <span style={{ fontSize: 22, fontWeight: 700, color: '#52c41a' }}>No.{i + 1}</span>
                </Tooltip>
                <div>
                  <Button size="small" type="link" onClick={() => navigate(`/knowledge?code=${r.code}`)}>查看图谱</Button>
                </div>
              </Col>
            </Row>

            <div style={{ marginBottom: 12 }}>
              {(r.tags || []).map((t) => <Tag color="blue" key={t}>{t}</Tag>)}
              <Tag>评分 {r.totalScore}</Tag>
            </div>

            {(r.concepts?.length ?? 0) > 0 && (
              <div style={{ marginBottom: 12 }}>
                <span style={{ fontSize: 12, color: '#999', marginRight: 6 }}>题材概念：</span>
                {r.concepts.slice(0, 12).map((c) =>
                  (r.hotConcepts || []).includes(c)
                    ? <Tag color="red" key={c}>🔥 {c}</Tag>
                    : <Tag color="orange" key={c}>{c}</Tag>
                )}
              </div>
            )}

            <Row gutter={8} style={{ marginBottom: 12 }}>
              <DataItem label="收盘价" value={(r.close ?? 0).toFixed(2)} />
              <DataItem label="今日涨幅" value={pct(r.changePercent)} color={upDown(r.changePercent)} />
              <DataItem label="市值" value={yi(r.totalMarketCap)} />
              <DataItem label="20日涨幅" value={pct(r.rise20d)} color={upDown(r.rise20d)} />
              <DataItem label="PE(TTM)" value={r.peTtm > 0 ? r.peTtm.toFixed(2) : '—'} />
            </Row>

            <div style={{ background: '#fafafa', padding: '8px 12px', borderRadius: 4, fontSize: 13, color: '#555' }}>
              <b>核心逻辑：</b>{r.coreLogic}
            </div>
          </Card>
        ))
      )}
    </div>
  );
};

export default Selection;
