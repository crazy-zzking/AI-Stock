import React, { useEffect, useState } from 'react';
import { Card, Table, Tag, Row, Col, Spin, message, List, Statistic, Rate } from 'antd';
import { getSelectionHistory, getSelectionPerformance } from '../api';
import Delta from '../components/Delta';
import { pct, upDownColor } from '../utils/format';

const redNum = (v?: number | null) => <span style={{ color: '#cf1322', fontVariantNumeric: 'tabular-nums' }}>{pct(v)}</span>;
const greenNum = (v?: number | null) => <span style={{ color: '#3f8600', fontVariantNumeric: 'tabular-nums' }}>{pct(v)}</span>;

interface HistoryItem { id: number; tradingDate: string; runAt: string; topN: number; }
interface PerfItem {
  code: string; name: string; selectClose: number; selectChangePercent: number;
  nextDayChangePercent?: number | null; currentChangePercent?: number | null;
  maxRisePercent?: number | null; maxDropPercent?: number | null; forwardDays: number;
  coreLogic?: string; tags?: string[]; ratingStars?: number; totalScore?: number;
}
interface Perf {
  id: number; selectionTradingDate: string; runAt: string; count: number;
  latestDate?: string | null; hitCount: number; avgCurrentChange: number; items: PerfItem[];
}

const SelectionHistory: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [list, setList] = useState<HistoryItem[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [perf, setPerf] = useState<Perf | null>(null);
  const [perfLoading, setPerfLoading] = useState(false);

  const loadList = async () => {
    setLoading(true);
    try {
      const res = await getSelectionHistory(50);
      const data: HistoryItem[] = res.data || [];
      setList(data);
      if (data.length > 0) selectBatch(data[0].id);
    } catch {
      message.error('加载选股历史失败');
    } finally {
      setLoading(false);
    }
  };

  const selectBatch = async (id: number) => {
    setSelectedId(id);
    setPerfLoading(true);
    try {
      const res = await getSelectionPerformance(id);
      setPerf(res.data || null);
    } catch {
      setPerf(null);
      message.error('加载选后表现失败');
    } finally {
      setPerfLoading(false);
    }
  };

  useEffect(() => { loadList(); /* eslint-disable-next-line */ }, []);

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  const columns = [
    { title: '名称', key: 'name', render: (_: unknown, r: PerfItem) => <span>{r.name} <span style={{ color: '#999', fontSize: 12 }}>{r.code}</span></span> },
    { title: '选股日涨幅', dataIndex: 'selectChangePercent', render: (v: number) => <Delta value={v} /> },
    { title: '次日(T+1)', dataIndex: 'nextDayChangePercent', render: (v: number | null) => <Delta value={v} /> },
    { title: '至今累计', dataIndex: 'currentChangePercent', render: (v: number | null) => <Delta value={v} bold /> },
    { title: '最高涨幅', dataIndex: 'maxRisePercent', render: (v: number | null) => redNum(v) },
    { title: '最低跌幅', dataIndex: 'maxDropPercent', render: (v: number | null) => greenNum(v) },
    { title: '观察天数', dataIndex: 'forwardDays', render: (v: number) => (v > 0 ? `${v}日` : '—') },
  ];

  return (
    <div>
      <h2>历史选股 · 选后表现跟踪</h2>
      <Row gutter={16}>
        <Col xs={24} lg={6}>
          <Card title="历史批次" size="small">
            <List
              size="small"
              dataSource={list}
              locale={{ emptyText: '暂无选股记录（先在选股页「重新选股」）' }}
              renderItem={(it) => (
                <List.Item
                  onClick={() => selectBatch(it.id)}
                  style={{ cursor: 'pointer', background: selectedId === it.id ? '#e6f4ff' : undefined, padding: '8px 12px' }}
                >
                  <div>
                    <div>{it.tradingDate?.slice(0, 10)} <Tag>TOP{it.topN}</Tag></div>
                    <div style={{ fontSize: 12, color: '#999' }}>选于 {it.runAt?.slice(0, 16).replace('T', ' ')}</div>
                  </div>
                </List.Item>
              )}
            />
          </Card>
        </Col>
        <Col xs={24} lg={18}>
          <Card
            size="small"
            title={perf
              ? `${perf.selectionTradingDate?.slice(0, 10)} 选的 ${perf.count} 只 · 至今 ${perf.hitCount} 红 · 均 ${pct(perf.avgCurrentChange)}（截至 ${perf.latestDate?.slice(0, 10) || '—'}）`
              : '选后表现'}
          >
            {perfLoading ? (
              <Spin style={{ display: 'block', margin: '40px auto' }} />
            ) : (
              <>
                <Row gutter={16} style={{ marginBottom: 12 }}>
                  <Col xs={8}><Statistic title="标的数" value={perf?.count ?? 0} /></Col>
                  <Col xs={8}><Statistic title="至今上涨" value={perf?.hitCount ?? 0} suffix={`/ ${perf?.count ?? 0}`} valueStyle={{ color: '#cf1322' }} /></Col>
                  <Col xs={8}><Statistic title="平均累计" value={pct(perf?.avgCurrentChange)} valueStyle={{ color: upDownColor(perf?.avgCurrentChange) }} /></Col>
                </Row>
                <Table
                  size="small" rowKey="code" pagination={false}
                  dataSource={perf?.items || []}
                  columns={columns}
                  expandable={{
                    expandedRowRender: (r: PerfItem) => (
                      <div style={{ padding: '4px 8px' }}>
                        <div style={{ marginBottom: 6 }}>
                          <Rate disabled value={r.ratingStars ?? 0} style={{ fontSize: 13 }} />
                          {typeof r.totalScore === 'number' && <Tag style={{ marginLeft: 8 }}>评分 {r.totalScore}</Tag>}
                        </div>
                        {(r.tags?.length ?? 0) > 0 && (
                          <div style={{ marginBottom: 6 }}>
                            {r.tags!.map((t) => <Tag color="blue" key={t}>{t}</Tag>)}
                          </div>
                        )}
                        <div style={{ background: '#fafafa', padding: '6px 10px', borderRadius: 4, fontSize: 13, color: '#555' }}>
                          <b>核心逻辑：</b>{r.coreLogic || '—'}
                        </div>
                      </div>
                    ),
                    rowExpandable: (r: PerfItem) => !!(r.coreLogic || (r.tags?.length ?? 0) > 0),
                  }}
                  locale={{ emptyText: '该批暂无前进数据（选股当日之后还没有K线，或K线未同步）' }}
                />
                <div style={{ marginTop: 8, color: '#999', fontSize: 12 }}>
                  说明：以选股日收盘价为基准；最高涨幅/最低跌幅取选中之后区间最高价/最低价相对基准的幅度。需 kline_data 有选股日之后的日K。
                </div>
              </>
            )}
          </Card>
        </Col>
      </Row>
    </div>
  );
};

export default SelectionHistory;
