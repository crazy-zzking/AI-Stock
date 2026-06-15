import React, { useEffect, useRef, useState } from 'react';
import { Card, Table, Tag, Row, Col, Spin, message, List, Statistic, Rate, Button, Pagination } from 'antd';
import { getSelectionHistoryPage, getSelectionPerformance, getQuote, reviewSelectionBatch } from '../api';
import Delta from '../components/Delta';
import ReviewPanel from '../components/ReviewPanel';
import { pct, upDownColor } from '../utils/format';
import type { QuoteData, LlmReview } from '../types/models';

const redNum = (v?: number | null) => <span style={{ color: '#cf1322', fontVariantNumeric: 'tabular-nums' }}>{pct(v)}</span>;
const greenNum = (v?: number | null) => <span style={{ color: '#3f8600', fontVariantNumeric: 'tabular-nums' }}>{pct(v)}</span>;

interface HistoryItem { id: number; tradingDate: string; runAt: string; topN: number; strategy?: string; strategyName?: string; reviewStatus?: string; }
interface PerfItem {
  code: string; name: string; selectClose: number; selectChangePercent: number;
  nextDayChangePercent?: number | null; currentChangePercent?: number | null;
  maxRisePercent?: number | null; maxDropPercent?: number | null; forwardDays: number;
  coreLogic?: string; tags?: string[]; ratingStars?: number; totalScore?: number;
  review?: LlmReview | null;
}
interface Perf {
  id: number; selectionTradingDate: string; runAt: string; count: number;
  latestDate?: string | null; hitCount: number; avgCurrentChange: number; items: PerfItem[];
  strategy?: string; strategyName?: string; reviewStatus?: string;
}

const fmtTime = (s?: string) => s?.slice(0, 16).replace('T', ' ') || '—';

// 复评状态 → 文案/颜色
const REVIEW_STATUS: Record<string, { text: string; color: string }> = {
  pending: { text: '待复评', color: 'default' },
  running: { text: 'LLM 复评中…', color: 'processing' },
  done: { text: '已复评', color: 'success' },
  failed: { text: '复评失败', color: 'error' },
  skipped: { text: '未复评', color: 'default' },
};

const SelectionHistory: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [list, setList] = useState<HistoryItem[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [perf, setPerf] = useState<Perf | null>(null);
  const [perfLoading, setPerfLoading] = useState(false);
  const [quotes, setQuotes] = useState<Record<string, QuoteData>>({});
  const [quoteLoading, setQuoteLoading] = useState<Record<string, boolean>>({});
  const [reviewing, setReviewing] = useState(false);
  const [expandedKeys, setExpandedKeys] = useState<React.Key[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [total, setTotal] = useState(0);
  const pollRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // 展开行时懒加载实时报价（已缓存则跳过）
  const loadQuote = async (code: string) => {
    if (!code || quotes[code] || quoteLoading[code]) return;
    setQuoteLoading((m) => ({ ...m, [code]: true }));
    try {
      // 用腾讯接口取实时报价（比东财快）
      const res = await getQuote(code, 'tencent');
      const q = res.data;
      if (q) setQuotes((m) => ({ ...m, [code]: q }));
    } catch {
      /* 静默：展开区会显示"实时报价获取失败" */
    } finally {
      setQuoteLoading((m) => ({ ...m, [code]: false }));
    }
  };

  const loadList = async (p = page, ps = pageSize, autoSelect = false) => {
    setLoading(true);
    try {
      const res = await getSelectionHistoryPage(p, ps);
      const data: HistoryItem[] = res.data?.items || [];
      setList(data);
      setTotal(res.data?.total ?? 0);
      // 仅首次加载自动选中本页第一条；翻页时保留当前选中，不打断右侧
      if (autoSelect && data.length > 0) selectBatch(data[0].id);
    } catch {
      message.error('加载选股历史失败');
    } finally {
      setLoading(false);
    }
  };

  const selectBatch = async (id: number, silent = false) => {
    setSelectedId(id);
    if (!silent) { setPerfLoading(true); setExpandedKeys([]); }
    try {
      const res = await getSelectionPerformance(id);
      const data: Perf | null = res.data || null;
      setPerf(data);
      // 复评进行中时轮询刷新，直到 done/failed/skipped
      if (pollRef.current) { clearTimeout(pollRef.current); pollRef.current = null; }
      if (data && (data.reviewStatus === 'running' || data.reviewStatus === 'pending')) {
        pollRef.current = setTimeout(() => selectBatch(id, true), 5000);
      }
    } catch {
      if (!silent) { setPerf(null); message.error('加载选后表现失败'); }
    } finally {
      if (!silent) setPerfLoading(false);
    }
  };

  // 手动（重新）触发当前批次 LLM 复评
  const triggerReview = async () => {
    if (selectedId == null) return;
    setReviewing(true);
    try {
      await reviewSelectionBatch(selectedId);
      message.success('已触发 LLM 复评，完成后自动刷新');
      selectBatch(selectedId, true);
    } catch {
      message.error('触发复评失败');
    } finally {
      setReviewing(false);
    }
  };

  // 全部展开 / 全部收起
  const toggleExpandAll = () => {
    const items = perf?.items || [];
    if (expandedKeys.length >= items.length && items.length > 0) {
      setExpandedKeys([]);
    } else {
      const codes = items.filter((i) => !!i.code).map((i) => i.code);
      setExpandedKeys(codes);
      codes.forEach((c) => loadQuote(c));
    }
  };

  useEffect(() => { loadList(1, pageSize, true); return () => { if (pollRef.current) clearTimeout(pollRef.current); }; /* eslint-disable-next-line */ }, []);

  const onPageChange = (p: number, ps: number) => {
    setPage(p);
    setPageSize(ps);
    loadList(p, ps);
  };

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
          <Card
            title="历史批次"
            size="small"
            styles={{ body: { padding: 0 } }}
            style={{ position: 'sticky', top: 12 }}
          >
            <div style={{ maxHeight: 'calc(100vh - 200px)', overflowY: 'auto' }}>
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
                      <div>
                        {it.tradingDate?.slice(0, 10)}
                        {it.strategyName && <Tag color="purple" style={{ marginLeft: 6 }}>{it.strategyName}</Tag>}
                        <Tag style={{ marginLeft: 2 }}>TOP{it.topN}</Tag>
                      </div>
                      <div style={{ fontSize: 12, color: '#999' }}>发起于 {fmtTime(it.runAt)}</div>
                    </div>
                  </List.Item>
                )}
              />
            </div>
            <div style={{ padding: '8px 12px', borderTop: '1px solid #f0f0f0', textAlign: 'center' }}>
              <Pagination
                size="small"
                current={page}
                pageSize={pageSize}
                total={total}
                onChange={onPageChange}
                showSizeChanger
                pageSizeOptions={[20, 50, 100]}
                showTotal={(t) => `共 ${t} 批`}
              />
            </div>
          </Card>
        </Col>
        <Col xs={24} lg={18}>
          <Card
            size="small"
            title={perf ? (
              <span>
                {perf.strategyName && <Tag color="purple">{perf.strategyName}</Tag>}
                {`${perf.selectionTradingDate?.slice(0, 10)} 选的 ${perf.count} 只 · 至今 ${perf.hitCount} 红 · 均 ${pct(perf.avgCurrentChange)}`}
                <span style={{ fontWeight: 400, color: '#999', fontSize: 12, marginLeft: 8 }}>
                  发起于 {fmtTime(perf.runAt)}
                </span>
              </span>
            ) : '选后表现'}
            extra={perf && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                <Button
                  size="small"
                  onClick={toggleExpandAll}
                  disabled={(perf.items?.length ?? 0) === 0}
                >
                  {expandedKeys.length >= (perf.items?.length ?? 0) && (perf.items?.length ?? 0) > 0 ? '全部收起' : '全部展开'}
                </Button>
                <Tag color={REVIEW_STATUS[perf.reviewStatus || 'pending']?.color}>
                  {REVIEW_STATUS[perf.reviewStatus || 'pending']?.text || perf.reviewStatus}
                </Tag>
                <Button
                  size="small"
                  loading={reviewing || perf.reviewStatus === 'running'}
                  onClick={triggerReview}
                >
                  {perf.reviewStatus === 'done' ? '重新复评' : 'LLM 复评'}
                </Button>
              </span>
            )}
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
                    expandedRowKeys: expandedKeys,
                    onExpandedRowsChange: (keys) => setExpandedKeys([...keys]),
                    onExpand: (expanded, r: PerfItem) => { if (expanded) loadQuote(r.code); },
                    expandedRowRender: (r: PerfItem) => {
                      const q = quotes[r.code];
                      return (
                        <div style={{ padding: '4px 8px' }}>
                          <div style={{ marginBottom: 8, display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
                            <b>实时报价：</b>
                            {quoteLoading[r.code] ? (
                              <Spin size="small" />
                            ) : q ? (
                              <>
                                <span>现价 <Delta value={q.price} mode="raw" colored={false} /></span>
                                <span>涨幅 <Delta value={q.changePercent} bold /></span>
                                <span>涨跌 <Delta value={q.changeAmount} mode="raw" /></span>
                                <span style={{ color: '#999', fontSize: 12 }}>
                                  {q.source} · {q.timestamp?.slice(11, 16) || q.timestamp}
                                </span>
                              </>
                            ) : (
                              <span style={{ color: '#999' }}>实时报价获取失败（非交易时段或数据源异常）</span>
                            )}
                          </div>
                          <div style={{ marginBottom: 6 }}>
                            <Rate disabled value={r.ratingStars ?? 0} style={{ fontSize: 13 }} />
                            {typeof r.totalScore === 'number' && <Tag style={{ marginLeft: 8 }}>评分 {r.totalScore}</Tag>}
                          </div>
                          {(r.tags?.length ?? 0) > 0 && (
                            <div style={{ marginBottom: 6 }}>
                              {r.tags!.map((t) => <Tag color="blue" key={t}>{t}</Tag>)}
                            </div>
                          )}
                          <div style={{ background: '#fafafa', padding: '6px 10px', borderRadius: 4, fontSize: 13, color: '#555', marginBottom: 8 }}>
                            <b>核心逻辑：</b>{r.coreLogic || '—'}
                          </div>
                          <ReviewPanel review={r.review} />
                        </div>
                      );
                    },
                    rowExpandable: (r: PerfItem) => !!r.code,
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
