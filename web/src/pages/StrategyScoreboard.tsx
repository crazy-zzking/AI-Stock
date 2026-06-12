import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert, Button, Card, Select, Space, Table, Tag, Tooltip, message,
} from 'antd';
import { ReloadOutlined, SyncOutlined, TrophyOutlined } from '@ant-design/icons';
import {
  getScoreboardDetails, getScoreboardSummary, syncScoreboard,
} from '../api';
import type { HorizonStatsDto, ScoreboardDetailDto, ScoreboardSummaryDto } from '../api';
import { upDownColor as upDown } from '../utils/format';

const fmtPct = (v: number | null | undefined) =>
  v == null ? '-' : `${v > 0 ? '+' : ''}${v.toFixed(2)}%`;

/** 单窗口（T+1/3/5）三联展示：胜率 / 均收益 / 均超额 */
const HorizonCell: React.FC<{ h: HorizonStatsDto }> = ({ h }) => {
  if (!h || h.count === 0) return <span style={{ color: '#999' }}>-</span>;
  return (
    <Tooltip title={`样本 ${h.count}，盈亏比 ${h.profitFactor ?? '-'}`}>
      <Space size={6}>
        <span>{h.winRate?.toFixed(0)}%</span>
        <span style={{ color: upDown(h.avgRet ?? 0), fontWeight: 600 }}>{fmtPct(h.avgRet)}</span>
        <span style={{ color: upDown(h.avgExcess ?? 0), fontSize: 12 }}>超额{fmtPct(h.avgExcess)}</span>
      </Space>
    </Tooltip>
  );
};

/** 策略记分板 — 真实选股信号（selection_performance）的 T+1/3/5 前向收益与沪深300超额，按策略聚合 */
const StrategyScoreboard: React.FC = () => {
  const [days, setDays] = useState(30);
  const [regime, setRegime] = useState<string | undefined>();
  const [summary, setSummary] = useState<ScoreboardSummaryDto[]>([]);
  const [details, setDetails] = useState<ScoreboardDetailDto[]>([]);
  const [strategy, setStrategy] = useState<string | undefined>();
  const [loading, setLoading] = useState(false);
  const [syncing, setSyncing] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [s, d] = await Promise.all([
        getScoreboardSummary(days, regime),
        getScoreboardDetails(strategy, days),
      ]);
      setSummary(s.data);
      setDetails(d.data);
    } catch {
      message.error('加载绩效数据失败，请确认后端可用');
    } finally {
      setLoading(false);
    }
  }, [days, regime, strategy]);

  useEffect(() => { load(); }, [load]);

  const sync = async () => {
    setSyncing(true);
    try {
      const res = await syncScoreboard();
      message.success(`已同步：新增 ${res.data.created}，更新 ${res.data.updated}，完成 ${res.data.finalized}`);
      await load();
    } catch {
      message.error('同步失败');
    } finally {
      setSyncing(false);
    }
  };

  const strategyOptions = useMemo(
    () => summary.map((s) => ({ value: s.strategy, label: `${s.strategyName} (${s.strategy})` })),
    [summary],
  );

  const summaryColumns = [
    {
      title: '策略', dataIndex: 'strategyName', key: 'strategyName', width: 180,
      render: (v: string, r: ScoreboardSummaryDto) => (
        <Space size={4}>
          <a onClick={() => setStrategy(r.strategy)}>{v || r.strategy}</a>
          {r.configVersion && (
            <Tooltip title="选股时生效的配置版本（策略迭代前后成绩分段对照）">
              <Tag color={r.configVersion === 'default' ? undefined : 'blue'}>{r.configVersion}</Tag>
            </Tooltip>
          )}
        </Space>
      ),
    },
    { title: '信号', dataIndex: 'signals', key: 'signals', width: 70 },
    {
      title: '一字板', dataIndex: 'untradable', key: 'untradable', width: 80,
      render: (v: number) => (v > 0 ? <Tag color="orange">{v}</Tag> : 0),
    },
    { title: '待K线', dataIndex: 'pending', key: 'pending', width: 70 },
    { title: 'T+1（胜率/均收/超额）', dataIndex: 'horizon1', key: 'h1', render: (h: HorizonStatsDto) => <HorizonCell h={h} /> },
    { title: 'T+3', dataIndex: 'horizon3', key: 'h3', render: (h: HorizonStatsDto) => <HorizonCell h={h} /> },
    { title: 'T+5', dataIndex: 'horizon5', key: 'h5', render: (h: HorizonStatsDto) => <HorizonCell h={h} /> },
  ];

  const retCol = (title: string, retKey: keyof ScoreboardDetailDto, excessKey: keyof ScoreboardDetailDto) => ({
    title, dataIndex: retKey as string, key: retKey as string, width: 100,
    sorter: (a: ScoreboardDetailDto, b: ScoreboardDetailDto) =>
      ((a[retKey] as number | null) ?? -999) - ((b[retKey] as number | null) ?? -999),
    render: (_: unknown, r: ScoreboardDetailDto) => {
      const ret = r[retKey] as number | null;
      const ex = r[excessKey] as number | null;
      if (ret == null) return <span style={{ color: '#999' }}>-</span>;
      return (
        <Tooltip title={`超额 ${fmtPct(ex)}`}>
          <span style={{ color: upDown(ret), fontWeight: 600 }}>{fmtPct(ret)}</span>
        </Tooltip>
      );
    },
  });

  const detailColumns = [
    { title: '信号日', dataIndex: 'tradingDate', key: 'tradingDate', width: 105, render: (v: string) => v?.slice(0, 10) },
    { title: '策略', dataIndex: 'strategyName', key: 'strategyName', width: 110, ellipsis: true },
    { title: '代码', dataIndex: 'code', key: 'code', width: 85 },
    { title: '名称', dataIndex: 'name', key: 'name', width: 100, ellipsis: true },
    { title: '得分', dataIndex: 'score', key: 'score', width: 70, render: (v: number) => v?.toFixed(1) },
    {
      title: '入场价', dataIndex: 'entryPrice', key: 'entryPrice', width: 90,
      render: (v: number | null, r: ScoreboardDetailDto) =>
        r.untradable ? <Tag color="orange">一字板</Tag> : (v == null ? '-' : v.toFixed(2)),
    },
    retCol('T+1', 'ret1', 'excess1'),
    retCol('T+3', 'ret3', 'excess3'),
    retCol('T+5', 'ret5', 'excess5'),
    {
      title: '状态', dataIndex: 'status', key: 'status', width: 80,
      render: (v: string) => v === 'final'
        ? <Tag color="green">已完成</Tag>
        : v === 'partial' ? <Tag color="blue">进行中</Tag> : <Tag>待K线</Tag>,
    },
  ];

  return (
    <div>
      <h2><TrophyOutlined /> 策略记分板</h2>
      <Alert
        type="info"
        style={{ marginBottom: 16 }}
        title="基于真实选股留痕（每交易日每策略最新一批信号）的不可篡改成绩单：T+1 开盘买入，T+1/T+3/T+5 收盘统计收益与沪深300同窗口超额；一字板买不进的信号单独剔除。数据由 Worker 的 selection-performance 任务每日收盘后补算。"
      />

      <Card style={{ marginBottom: 16 }}>
        <Space wrap>
          <span>统计窗口</span>
          <Select
            value={days}
            onChange={setDays}
            style={{ width: 120 }}
            options={[
              { value: 7, label: '近 7 天' },
              { value: 30, label: '近 30 天' },
              { value: 60, label: '近 60 天' },
              { value: 90, label: '近 90 天' },
            ]}
          />
          <span>大盘环境</span>
          <Select
            allowClear
            placeholder="全部环境"
            value={regime}
            onChange={setRegime}
            style={{ width: 120 }}
            options={[
              { value: 'weak', label: '弱市' },
              { value: 'neutral', label: '中性' },
              { value: 'strong', label: '强市' },
            ]}
          />
          <Button icon={<ReloadOutlined />} onClick={load} loading={loading}>刷新</Button>
          <Tooltip title="立即物化最新选股并用已入库K线补算（平时每日自动执行）">
            <Button icon={<SyncOutlined />} onClick={sync} loading={syncing}>补算</Button>
          </Tooltip>
        </Space>
      </Card>

      <Card title="按策略聚合" style={{ marginBottom: 16 }}>
        <Table
          rowKey={(r) => `${r.strategy}@${r.configVersion}`}
          size="small"
          columns={summaryColumns}
          dataSource={summary}
          loading={loading}
          pagination={false}
        />
      </Card>

      <Card
        title="信号明细"
        extra={(
          <Select
            allowClear
            placeholder="全部策略"
            value={strategy}
            onChange={setStrategy}
            style={{ width: 220 }}
            options={strategyOptions}
          />
        )}
      >
        <Table
          rowKey="id"
          size="small"
          columns={detailColumns}
          dataSource={details}
          loading={loading}
          pagination={{ pageSize: 20, showSizeChanger: true }}
        />
      </Card>
    </div>
  );
};

export default StrategyScoreboard;
