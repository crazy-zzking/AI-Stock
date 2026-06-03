import React, { useEffect, useState } from 'react';
import {
  Card, Rate, Tag, Row, Col, Alert, Spin, message, Button,
  InputNumber, Space, Typography, Tooltip, Select,
} from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { getLatestSelection, rerunSelection, getStrategies } from '../api';
import type { StrategyInfo } from '../api';
import { pct, upDownColor as upDown } from '../utils/format';

interface FactorScores {
  capital: number; technical: number; position: number;
  dragonTiger: number; activity: number; form: number; theme: number; sector: number;
}
interface SelectionResult {
  code: string; name: string; industry: string; concepts: string[]; hotConcepts: string[];
  ratingStars: number; tags: string[];
  close: number; changePercent: number; totalMarketCap: number;
  rise20d: number; peTtm: number; mainNetInflow: number;
  totalScore: number; factors: FactorScores; coreLogic: string;
  marketRegime?: string; recommendedStrategy?: string;
}

// 市值用「X.X亿」，无值显示 —（与全局 yi 略不同：单档亿、1 位小数）
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
  const [rerunning, setRerunning] = useState(false);
  const [topN, setTopN] = useState(5);
  const [list, setList] = useState<SelectionResult[]>([]);
  const [strategies, setStrategies] = useState<StrategyInfo[]>([]);
  const [strategy, setStrategy] = useState<string>('lowdip');

  const currentStrategy = strategies.find((s) => s.key === strategy);

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

  // 重新选股：按所选策略重算并追加一条历史记录（区别于「刷新」只读最近一次记录）
  const rerun = async (n: number) => {
    setRerunning(true);
    try {
      const res = await rerunSelection({ topN: n }, strategy);
      setList(res.data || []);
      message.success(`已用「${currentStrategy?.name ?? strategy}」策略重新选股`);
    } catch {
      message.error('重新选股失败');
    } finally {
      setRerunning(false);
    }
  };

  useEffect(() => {
    load(topN);
    getStrategies().then((res) => setStrategies(res.data || [])).catch(() => {});
    /* eslint-disable-next-line */
  }, []);

  return (
    <div>
      <h2 style={{ marginBottom: 12 }}>
        明日可介入 — 短线弹性品种 TOP{topN}
        <Space style={{ marginLeft: 16 }}>
          <Tooltip title={currentStrategy ? `${currentStrategy.description}（适用：${currentStrategy.preferredRegime}）` : '选择选股策略'}>
            <Select
              size="small"
              style={{ width: 130 }}
              value={strategy}
              onChange={setStrategy}
              options={strategies.map((s) => ({ value: s.key, label: s.name }))}
              placeholder="策略"
            />
          </Tooltip>
          <InputNumber min={1} max={20} value={topN} onChange={(v) => setTopN(v || 5)} size="small" style={{ width: 70 }} />
          <Button icon={<ReloadOutlined />} size="small" onClick={() => load(topN)}>刷新</Button>
          <Tooltip title="按所选策略重新选股并新增一条记录（不覆盖历史；刷新只读最近一次记录，盘中不跳动）">
            <Button size="small" type="primary" loading={rerunning} onClick={() => rerun(topN)}>重新选股</Button>
          </Tooltip>
        </Space>
      </h2>

      {list[0]?.marketRegime && (
        <Alert
          type={list[0].marketRegime.includes('偏弱') ? 'error' : list[0].marketRegime.includes('偏强') ? 'success' : 'info'}
          showIcon
          style={{ marginBottom: 12 }}
          message={list[0].marketRegime}
        />
      )}

      {(() => {
        const rec = list[0]?.recommendedStrategy;
        if (!rec || rec === strategy) return null;
        const recName = strategies.find((s) => s.key === rec)?.name ?? rec;
        return (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            message={`当前市场状态建议使用「${recName}」策略（你当前选的是「${currentStrategy?.name ?? strategy}」）`}
            action={<Button size="small" type="primary" onClick={() => setStrategy(rec)}>切换为{recName}</Button>}
          />
        );
      })()}

      <Alert
        type="warning"
        showIcon
        style={{ marginBottom: 16 }}
        message="筛选逻辑（埋伏型）：温和放量未涨停（规避追高/次日高开）+ 主力净流入为正 + 技术面多头未超买 + 20日涨幅<50%（低位）+ 龙虎榜阵容优质；排除「传统低弹性行业(金融/地产/电力/采矿/建筑/交运等) 且 市值>500亿」的大盘股(科技成长大票/小盘传统仍保留)；并结合大盘指数环境动态收紧/放宽"
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
                <Tooltip title={`资金${r.factors.capital}/技术${r.factors.technical}/位置${r.factors.position}/形态${r.factors.form}/龙虎${r.factors.dragonTiger}/活跃${r.factors.activity}/题材${r.factors.theme}/板块${r.factors.sector}`}>
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
