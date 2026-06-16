import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Card, Tabs, Table, Switch, InputNumber, Button, Form, Input, Select,
  message, Tag, Tooltip, Space, Alert, Badge,
} from 'antd';
import { ReloadOutlined, SaveOutlined, PlayCircleOutlined } from '@ant-design/icons';
import {
  getWorkerJobs, saveWorkerJobs, listWorkerSections, getWorkerSection, saveWorkerSection,
  getWorkerJobsStatus, runWorkerJob,
  type WorkerJobConfig, type WorkerSectionMeta, type WorkerJobStatus,
} from '../api';

/** 毫秒耗时格式化 */
const fmtDuration = (ms?: number | null): string => {
  if (ms == null) return '-';
  if (ms < 1000) return `${ms}ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)}s`;
  const m = Math.floor(s / 60);
  return `${m}m${Math.round(s - m * 60)}s`;
};

/** 时间格式化（本地 HH:mm:ss / 含日期） */
const fmtTime = (iso?: string | null): string => {
  if (!iso) return '-';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '-';
  const today = new Date();
  const sameDay = d.toDateString() === today.toDateString();
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  const ss = String(d.getSeconds()).padStart(2, '0');
  return sameDay ? `${hh}:${mm}:${ss}` : `${d.getMonth() + 1}/${d.getDate()} ${hh}:${mm}:${ss}`;
};

/** 业务参数段的字段布局（前端定义；后端按原始 JSON 存取） */
type FieldType = 'bool' | 'number' | 'text' | 'tags';
interface FieldDef { key: string; label: string; type: FieldType; help?: string }

const SECTION_FIELDS: Record<string, FieldDef[]> = {
  DataSync: [
    { key: 'Enabled', label: '启用数据同步', type: 'bool', help: 'false 则数据同步任务空转' },
    { key: 'StockCodesUrl', label: '股票池接口地址', type: 'text' },
    { key: 'WorkdayUrl', label: '交易日接口地址', type: 'text' },
    { key: 'SyncHour', label: '收盘同步时刻(0-23)', type: 'number', help: '交易日此刻后同步当日K线' },
    { key: 'SyncKlines', label: '同步K线', type: 'bool' },
    { key: 'KlineCount', label: '每只股票日K条数', type: 'number' },
    { key: 'MaxStocks', label: '每轮最多同步股票数', type: 'number', help: '0 = 全部' },
    { key: 'KlineThrottleMs', label: 'K线请求间隔(ms)', type: 'number', help: '仅串行(批大小≤1)时生效' },
    { key: 'KlineBatchSize', label: 'K线批大小', type: 'number', help: '批内并发；=1 退化串行' },
    { key: 'KlineBatchDelayMs', label: 'K线批间延迟(ms)', type: 'number' },
    { key: 'SyncDetails', label: '同步股票明细(行业/概念)', type: 'bool' },
    { key: 'DetailMaxStocks', label: '明细每轮最多处理', type: 'number', help: '0 = 全部' },
    { key: 'DetailBatchSize', label: '明细批大小', type: 'number' },
    { key: 'DetailBatchDelayMs', label: '明细批间延迟(ms)', type: 'number' },
  ],
  IntelligenceSync: [
    { key: 'Enabled', label: '启用情报采集', type: 'bool' },
    { key: 'IntervalMinutes', label: '采集周期(分钟)', type: 'number' },
    { key: 'CollectNews', label: '采集财经新闻', type: 'bool' },
    { key: 'NewsCount', label: '每轮新闻条数', type: 'number' },
    { key: 'CollectAnnouncements', label: '采集公司公告', type: 'bool' },
    { key: 'AnnouncementCount', label: '每轮公告条数', type: 'number' },
    { key: 'AnnouncementKeywords', label: '公告关键字过滤', type: 'tags', help: '留空用代码默认；仅标题命中才送 LLM 省 token' },
    { key: 'CollectReports', label: '采集研报', type: 'bool' },
    { key: 'ReportCount', label: '每轮研报条数', type: 'number' },
    { key: 'ItemThrottleMs', label: '每条 LLM 抽取间隔(ms)', type: 'number' },
  ],
  MarketSnapshot: [
    { key: 'ItemThrottleMs', label: '每只股票限流(ms)', type: 'number' },
    { key: 'MaxStocks', label: '单次最大股票数', type: 'number', help: '0 = 不限' },
    { key: 'EnableIntraday', label: '启用盘中采集', type: 'bool', help: '交易时段周期用腾讯批量刷新快照' },
    { key: 'IntradayIntervalMinutes', label: '盘中采集间隔(分钟)', type: 'number' },
    { key: 'IntradayQuoteBatch', label: '盘中批量报价每批股票数', type: 'number' },
    { key: 'CloseDecisionTime', label: '尾盘决策时点(HH:mm)', type: 'text', help: '盘中刷新对齐到此时点；空则不对齐' },
    { key: 'DragonTigerUrl', label: '龙虎榜接口地址', type: 'text' },
  ],
  KnowledgeStar: [
    { key: 'UseCli', label: '走官方 zsxq-cli', type: 'bool', help: '推荐 true：OAuth 密钥绕开签名/401' },
    { key: 'CliPath', label: 'zsxq-cli 路径', type: 'text' },
    { key: 'CliTimeoutSeconds', label: 'CLI 超时(秒)', type: 'number' },
    { key: 'AccessToken', label: 'access_token', type: 'text', help: '仅 UseCli=false：浏览器 cookie 复制' },
    { key: 'BaseUrl', label: 'API 基址', type: 'text' },
    { key: 'GroupIds', label: '星球 group_id', type: 'tags' },
    { key: 'Count', label: '每星球每次取条数', type: 'number' },
    { key: 'LookbackHours', label: '增量回看窗口(小时)', type: 'number' },
    { key: 'UserAgent', label: 'User-Agent', type: 'text' },
    { key: 'MinDelayMs', label: '请求前最小随机延迟(ms)', type: 'number', help: '防封' },
    { key: 'MaxDelayMs', label: '请求前最大随机延迟(ms)', type: 'number', help: '防封' },
  ],
  GraphPromotion: [
    { key: 'MinMentions', label: '最小提及次数', type: 'number', help: '被多少篇情报印证才晋升' },
    { key: 'MinCredibility', label: '最小可信度(0-100)', type: 'number' },
    { key: 'MaxPerRun', label: '单次最多晋升条数', type: 'number' },
  ],
};

/** 调度层：任务表格 */
const JobsTab: React.FC = () => {
  const [rows, setRows] = useState<WorkerJobConfig[]>([]);
  const [status, setStatus] = useState<Record<string, WorkerJobStatus>>({});
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [triggering, setTriggering] = useState<Record<string, boolean>>({});
  const [, setTick] = useState(0); // 驱动"运行中"耗时实时刷新
  const statusTimer = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await getWorkerJobs();
      setRows(data);
    } catch {
      message.error('加载任务调度配置失败');
    } finally {
      setLoading(false);
    }
  }, []);

  const loadStatus = useCallback(async () => {
    try {
      const { data } = await getWorkerJobsStatus();
      const map: Record<string, WorkerJobStatus> = {};
      data.forEach((s) => { map[s.name] = s; });
      setStatus(map);
    } catch { /* 轮询失败静默 */ }
  }, []);

  useEffect(() => { load(); loadStatus(); }, [load, loadStatus]);

  // 每 3s 轮询运行态；任一任务运行中时每 1s 刷新耗时显示
  useEffect(() => {
    statusTimer.current = setInterval(loadStatus, 3000);
    const tickTimer = setInterval(() => setTick((t) => t + 1), 1000);
    return () => {
      if (statusTimer.current) clearInterval(statusTimer.current);
      clearInterval(tickTimer);
    };
  }, [loadStatus]);

  const patch = (name: string, p: Partial<WorkerJobConfig>) =>
    setRows((rs) => rs.map((r) => (r.name === name ? { ...r, ...p } : r)));

  const save = async () => {
    setSaving(true);
    try {
      await saveWorkerJobs(rows);
      message.success('已保存，Worker 将在约 10 秒内热生效');
    } catch {
      message.error('保存失败');
    } finally {
      setSaving(false);
    }
  };

  const runNow = async (name: string) => {
    setTriggering((t) => ({ ...t, [name]: true }));
    try {
      const { data } = await runWorkerJob(name);
      message[data.running ? 'warning' : 'success'](data.message);
      setTimeout(loadStatus, 800);
    } catch {
      message.error('触发失败');
    } finally {
      setTriggering((t) => ({ ...t, [name]: false }));
    }
  };

  const columns = [
    {
      title: '任务', dataIndex: 'displayName', width: 200,
      render: (v: string, r: WorkerJobConfig) => (
        <Space direction="vertical" size={0}>
          <span>{v} {r.dynamic && <Tag color="purple">动态</Tag>}</span>
          <span style={{ fontSize: 12, color: '#999' }}>{r.name} · {r.hint}</span>
        </Space>
      ),
    },
    {
      title: '状态', dataIndex: 'name', width: 90,
      render: (name: string) => {
        const s = status[name];
        if (s?.isRunning) return <Badge status="processing" text="运行中" />;
        if (s?.lastSuccess === false) return <Tooltip title={s.lastError ?? ''}><Badge status="error" text="失败" /></Tooltip>;
        if (s?.lastSuccess === true) return <Badge status="success" text="空闲" />;
        return <Badge status="default" text="—" />;
      },
    },
    {
      title: '最近开始', dataIndex: 'name', width: 110,
      render: (name: string) => {
        const s = status[name];
        return <span style={{ fontSize: 12 }}>{fmtTime(s?.lastStart)}{s?.lastTrigger === 'Manual' && <Tag color="blue" style={{ marginLeft: 4 }}>手动</Tag>}</span>;
      },
    },
    {
      title: '耗时', dataIndex: 'name', width: 90,
      render: (name: string) => {
        const s = status[name];
        if (s?.isRunning && s.lastStart) {
          const elapsed = Date.now() - new Date(s.lastStart).getTime();
          return <span style={{ fontSize: 12, color: '#1677ff' }}>{fmtDuration(elapsed)}</span>;
        }
        return <span style={{ fontSize: 12 }}>{fmtDuration(s?.lastDurationMs)}</span>;
      },
    },
    {
      title: '立即运行', dataIndex: 'name', width: 96,
      render: (name: string) => (
        <Button size="small" icon={<PlayCircleOutlined />} loading={triggering[name]}
          disabled={status[name]?.isRunning}
          onClick={() => runNow(name)}>运行</Button>
      ),
    },
    {
      title: '启用', dataIndex: 'enabled', width: 70,
      render: (v: boolean, r: WorkerJobConfig) =>
        <Switch checked={v} onChange={(c) => patch(r.name, { enabled: c })} />,
    },
    {
      title: '启动即跑', dataIndex: 'runOnStartup', width: 84,
      render: (v: boolean, r: WorkerJobConfig) =>
        <Switch checked={v} onChange={(c) => patch(r.name, { runOnStartup: c })} />,
    },
    {
      title: '周期(秒)', dataIndex: 'intervalSeconds', width: 120,
      render: (v: number, r: WorkerJobConfig) => (
        <InputNumber min={0} value={v} style={{ width: 100 }}
          onChange={(n) => patch(r.name, { intervalSeconds: Number(n) || 0 })} />
      ),
    },
    {
      title: '每日定点(时:分)', dataIndex: 'dailyAtHour', width: 180,
      render: (_: number, r: WorkerJobConfig) => (
        <Tooltip title="时 0-23 时按每日定点运行（优先于周期）；时填 -1 表示不用。分 0-59。">
          <Space size={4}>
            <InputNumber min={-1} max={23} value={r.dailyAtHour} style={{ width: 68 }}
              onChange={(n) => patch(r.name, { dailyAtHour: n == null ? -1 : Number(n) })} />
            <span>:</span>
            <InputNumber min={0} max={59} value={r.dailyAtMinute} style={{ width: 68 }}
              disabled={r.dailyAtHour < 0}
              onChange={(n) => patch(r.name, { dailyAtMinute: n == null ? 0 : Number(n) })} />
          </Space>
        </Tooltip>
      ),
    },
  ];

  return (
    <>
      <Alert type="info" showIcon style={{ marginBottom: 12 }}
        message="调度层控制每个任务“何时跑/是否跑”。每日定点(时:分)优先于周期间隔；动态任务的间隔由任务自定，此处定点/周期仅作回退。「立即运行」立即触发一次（同一任务运行中不并发，约数秒内开始）。"
      />
      <Space style={{ marginBottom: 12 }}>
        <Button icon={<SaveOutlined />} type="primary" loading={saving} onClick={save}>保存</Button>
        <Button icon={<ReloadOutlined />} onClick={() => { load(); loadStatus(); }}>重新加载</Button>
      </Space>
      <Table rowKey="name" size="small" loading={loading} pagination={false}
        columns={columns} dataSource={rows} scroll={{ x: 1100 }} />
    </>
  );
};

/** 业务参数：单段表单 */
const SectionForm: React.FC<{ section: string }> = ({ section }) => {
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const fields = SECTION_FIELDS[section] ?? [];

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await getWorkerSection(section);
      form.resetFields();
      form.setFieldsValue(data);
    } catch {
      message.error('加载配置失败');
    } finally {
      setLoading(false);
    }
  }, [section, form]);

  useEffect(() => { load(); }, [load]);

  const save = async () => {
    setSaving(true);
    try {
      const values = form.getFieldsValue();
      await saveWorkerSection(section, values);
      message.success('已保存，Worker 将在下一轮运行使用新值');
    } catch {
      message.error('保存失败');
    } finally {
      setSaving(false);
    }
  };

  const renderInput = (f: FieldDef) => {
    switch (f.type) {
      case 'bool': return <Switch />;
      case 'number': return <InputNumber style={{ width: '100%', maxWidth: 200 }} />;
      case 'tags': return <Select mode="tags" style={{ width: '100%' }} tokenSeparators={[',']} placeholder="回车添加" />;
      default: return <Input style={{ width: '100%' }} />;
    }
  };

  return (
    <Form form={form} labelCol={{ flex: '200px' }} wrapperCol={{ flex: 'auto' }}
      labelAlign="left" style={{ maxWidth: 760 }}>
      <Space style={{ marginBottom: 12 }}>
        <Button icon={<SaveOutlined />} type="primary" loading={saving} onClick={save}>保存</Button>
        <Button icon={<ReloadOutlined />} loading={loading} onClick={load}>重新加载</Button>
      </Space>
      {fields.map((f) => (
        <Form.Item key={f.key} name={f.key} label={f.label} help={f.help}
          valuePropName={f.type === 'bool' ? 'checked' : 'value'}>
          {renderInput(f)}
        </Form.Item>
      ))}
    </Form>
  );
};

const WorkerConfig: React.FC = () => {
  const [sections, setSections] = useState<WorkerSectionMeta[]>([]);

  useEffect(() => {
    listWorkerSections().then((r) => setSections(r.data)).catch(() => {});
  }, []);

  const items = [
    { key: 'jobs', label: '任务调度', children: <JobsTab /> },
    ...sections.map((s) => ({
      key: s.section,
      label: s.displayName,
      children: <SectionForm section={s.section} />,
    })),
  ];

  return (
    <Card title="Worker 任务配置" styles={{ body: { paddingTop: 12 } }}>
      <Alert type="warning" showIcon style={{ marginBottom: 12 }}
        message="此处配置存于数据库，与 appsettings 解耦：Worker 启动时把 appsettings 默认值种子化进库，之后以本页为准、热生效（调度约 10 秒内、业务参数下一轮运行生效），改 appsettings 不再起作用。"
      />
      <Tabs items={items} />
    </Card>
  );
};

export default WorkerConfig;
