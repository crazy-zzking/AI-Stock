import React, { useEffect, useState } from 'react';
import {
  Card, Table, Tag, message, Button, Modal, Form, Input, InputNumber,
  Switch, Space, Popconfirm, Tooltip, Row, Col, Select, Alert,
} from 'antd';
import {
  SlidersOutlined, ReloadOutlined, SaveOutlined, RollbackOutlined,
  CheckCircleOutlined, EyeOutlined, DeleteOutlined, ThunderboltOutlined,
} from '@ant-design/icons';
import {
  getSelectionConfig, getSelectionConfigDefault, listSelectionConfig,
  saveSelectionConfig, activateSelectionConfig, deleteSelectionConfig, getStrategies,
} from '../api';
import type { SelectionConfigItem, SelectionCriteriaDto, StrategyInfo } from '../api';

const WEIGHT_FIELDS: { key: keyof SelectionCriteriaDto['weights']; label: string }[] = [
  { key: 'capital', label: '资金面' },
  { key: 'technical', label: '技术面' },
  { key: 'position', label: '位置' },
  { key: 'form', label: '形态' },
  { key: 'dragonTiger', label: '龙虎榜' },
  { key: 'activity', label: '活跃度' },
  { key: 'theme', label: '题材' },
  { key: 'sector', label: '板块' },
];

const SEC_TITLE: React.CSSProperties = {
  fontWeight: 600, color: '#1677ff', margin: '4px 0 16px',
  borderLeft: '3px solid #1677ff', paddingLeft: 8,
};

/** 选股配置中心 — 版本化的阈值 + 因子权重，可保存多版本并切换生效 */
const SelectionConfig: React.FC = () => {
  const [form] = Form.useForm<SelectionCriteriaDto>();
  const [list, setList] = useState<SelectionConfigItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [saveOpen, setSaveOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveForm] = Form.useForm();
  const [strategies, setStrategies] = useState<StrategyInfo[]>([]);
  const [strategy, setStrategy] = useState('lowdip');

  const currentStrategy = strategies.find((s) => s.key === strategy);

  // 实时监听 8 因子权重之和（按绝对权重加权，不强制=1，仅提示）
  const weights = Form.useWatch('weights', form);
  const weightSum = weights
    ? WEIGHT_FIELDS.reduce((s, f) => s + (Number(weights[f.key]) || 0), 0)
    : 0;

  useEffect(() => {
    getStrategies().then((r) => setStrategies(r.data || [])).catch(() => {});
    loadList();
    loadActive('lowdip');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onStrategyChange = (key: string) => {
    setStrategy(key);
    loadActive(key);
  };

  const loadList = async () => {
    setLoading(true);
    try {
      const res = await listSelectionConfig();
      setList(res.data || []);
    } catch {
      message.error('加载配置版本列表失败');
    } finally {
      setLoading(false);
    }
  };

  const loadActive = async (key = strategy) => {
    try {
      const res = await getSelectionConfig(key);
      form.setFieldsValue(res.data);
    } catch {
      message.error('加载生效配置失败');
    }
  };

  const loadDefault = async () => {
    try {
      const res = await getSelectionConfigDefault();
      form.setFieldsValue(res.data);
      message.success('已载入代码默认值（未保存）');
    } catch {
      message.error('加载默认配置失败');
    }
  };

  const viewVersion = (row: SelectionConfigItem) => {
    try {
      const criteria = JSON.parse(row.configJson) as SelectionCriteriaDto;
      form.setFieldsValue(criteria);
      message.success(`已载入 ${row.name}/${row.version} 到编辑区（未激活）`);
    } catch {
      message.error('该版本配置解析失败');
    }
  };

  const activate = async (id: number) => {
    try {
      await activateSelectionConfig(id);
      message.success('已激活');
      await loadList();
    } catch {
      message.error('激活失败');
    }
  };

  const remove = async (id: number) => {
    try {
      await deleteSelectionConfig(id);
      message.success('已删除');
      await loadList();
    } catch (e: any) {
      message.error(e?.response?.data || '删除失败（生效中的版本不可删）');
    }
  };

  const openSave = () => {
    saveForm.resetFields();
    saveForm.setFieldsValue({ name: strategy, version: '', remark: '', activate: true });
    setSaveOpen(true);
  };

  const doSave = async () => {
    try {
      const criteria = await form.validateFields();
      const meta = await saveForm.validateFields();
      setSaving(true);
      await saveSelectionConfig({
        name: meta.name, version: meta.version, remark: meta.remark,
        activate: !!meta.activate, criteria,
      });
      message.success(`已保存${meta.activate ? '并激活' : ''}：${meta.name}/${meta.version}`);
      setSaveOpen(false);
      await loadList();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data || '保存失败');
    } finally {
      setSaving(false);
    }
  };

  const columns = [
    { title: '配置名', dataIndex: 'name', key: 'name', width: 110 },
    { title: '版本', dataIndex: 'version', key: 'version', width: 90 },
    {
      title: '状态', dataIndex: 'isActive', key: 'isActive', width: 90,
      render: (v: boolean) =>
        v ? <Tag color="green" icon={<CheckCircleOutlined />}>生效中</Tag> : <Tag>未生效</Tag>,
    },
    { title: '备注', dataIndex: 'remark', key: 'remark', ellipsis: true },
    {
      title: '更新时间', dataIndex: 'updatedAt', key: 'updatedAt', width: 170,
      render: (v: string) => (v ? new Date(v).toLocaleString() : '-'),
    },
    {
      title: '操作', key: 'actions', width: 200,
      render: (_: unknown, row: SelectionConfigItem) => (
        <Space size="small">
          <Tooltip title="载入编辑区">
            <Button size="small" icon={<EyeOutlined />} onClick={() => viewVersion(row)} />
          </Tooltip>
          {!row.isActive && (
            <Popconfirm title="激活此版本？" onConfirm={() => activate(row.id)} okText="激活" cancelText="取消">
              <Button size="small" type="primary" icon={<ThunderboltOutlined />}>激活</Button>
            </Popconfirm>
          )}
          {!row.isActive && (
            <Popconfirm title="删除此版本？" onConfirm={() => remove(row.id)} okText="删除" cancelText="取消">
              <Tooltip title="删除"><Button size="small" danger icon={<DeleteOutlined />} /></Tooltip>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  const numItem = (name: any, label: string, props: Record<string, unknown> = {}) => (
    <Col span={6}>
      <Form.Item name={name} label={label} rules={[{ required: true, message: '必填' }]}>
        <InputNumber style={{ width: '100%' }} {...props} />
      </Form.Item>
    </Col>
  );

  return (
    <div>
      <h2><SlidersOutlined /> 选股配置中心</h2>
      <Alert
        type="info"
        style={{ marginBottom: 16 }}
        message="编辑下方参数后点「保存为新版本」存档；同一配置名可存多版本，「激活」的版本即选股实际使用的配置。权重按绝对值加权，不要求合计为 1。"
      />

      <Card
        title={
          <Space>
            参数编辑
            <Select
              size="small"
              style={{ width: 150 }}
              value={strategy}
              onChange={onStrategyChange}
              options={strategies.map((s) => ({ value: s.key, label: s.name }))}
            />
            {currentStrategy && (
              <Tooltip title={`${currentStrategy.description}（适用：${currentStrategy.preferredRegime}）`}>
                <Tag color="blue">{currentStrategy.preferredRegime}</Tag>
              </Tooltip>
            )}
          </Space>
        }
        style={{ marginBottom: 16 }}
        extra={
          <Space>
            <Button icon={<RollbackOutlined />} onClick={() => loadActive()}>载入生效配置</Button>
            <Button icon={<ReloadOutlined />} onClick={loadDefault}>恢复代码默认</Button>
            <Button type="primary" icon={<SaveOutlined />} onClick={openSave}>保存为新版本</Button>
          </Space>
        }
      >
        <Form form={form} layout="vertical">
          <div style={SEC_TITLE}>一级粗筛 / 硬过滤阈值</div>
          <Row gutter={16}>
            {numItem('topN', '返回 TOP-N', { min: 1, max: 50 })}
            {numItem('healthyRiseMin', '温和放量涨幅下限(%)', { step: 0.5 })}
            {numItem('healthyRiseMax', '追高线/涨幅上限(%)', { step: 0.5 })}
            {numItem('minVolumeRatio', '量比下限', { step: 0.1 })}
          </Row>
          <Row gutter={16}>
            {numItem('shockAmplitude', '放量震荡振幅(%)', { step: 0.5 })}
            {numItem('maxRsi', 'RSI 超买线', { min: 0, max: 100 })}
            {numItem('maxRise20d', '20日涨幅上限(%)', { step: 1 })}
            {numItem('minMainNetInflow', '主力净流入下限(元)', {
              step: 1000000,
              formatter: (v: any) => `${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ','),
              parser: (v: any) => v.replace(/,/g, ''),
            })}
          </Row>
          <Row gutter={16}>
            {numItem('maxTotalMarketCap', '传统大盘股市值阈值(元)', {
              step: 1000000000,
              formatter: (v: any) => `${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ','),
              parser: (v: any) => v.replace(/,/g, ''),
            })}
            <Col span={6}>
              <Form.Item name="requireDragonTiger" label="要求当日上龙虎榜" valuePropName="checked">
                <Switch checkedChildren="是" unCheckedChildren="否" />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="excludeTraditionalIndustry" label="排除传统大盘股" valuePropName="checked">
                <Switch checkedChildren="是" unCheckedChildren="否" />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item name="useLlmNarrative" label="LLM 生成核心逻辑" valuePropName="checked">
                <Switch checkedChildren="是" unCheckedChildren="否" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="excludeIndustryKeywords" label="传统行业关键字（命中即视为传统行业）">
            <Select mode="tags" style={{ width: '100%' }} placeholder="如 金融 / 银行 / 房地产 ..." />
          </Form.Item>

          <div style={SEC_TITLE}>
            多因子打分权重
            <Tag color={Math.abs(weightSum - 1) < 0.001 ? 'green' : 'orange'} style={{ marginLeft: 8 }}>
              8 因子合计 {weightSum.toFixed(2)}
            </Tag>
          </div>
          <Row gutter={16}>
            {WEIGHT_FIELDS.map((f) => (
              <Col span={6} key={f.key}>
                <Form.Item name={['weights', f.key]} label={f.label} rules={[{ required: true, message: '必填' }]}>
                  <InputNumber min={0} max={1} step={0.01} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
            ))}
          </Row>
          <div style={SEC_TITLE}>大盘环境系数</div>
          <Row gutter={16}>
            {numItem(['weights', 'regimeWeakFactor'], '弱市系数(<1收紧)', { min: 0, max: 2, step: 0.01 })}
            {numItem(['weights', 'regimeStrongFactor'], '强市系数(>1放宽)', { min: 0, max: 2, step: 0.01 })}
          </Row>
        </Form>
      </Card>

      <Card title="配置版本">
        <Table
          columns={columns}
          dataSource={list}
          rowKey="id"
          loading={loading}
          pagination={false}
          size="small"
        />
      </Card>

      <Modal
        title="保存为新版本"
        open={saveOpen}
        onOk={doSave}
        onCancel={() => setSaveOpen(false)}
        confirmLoading={saving}
        okText="保存"
        cancelText="取消"
        destroyOnClose
      >
        <Form form={saveForm} layout="vertical" style={{ marginTop: 12 }}>
          <Form.Item name="name" label="所属策略" rules={[{ required: true, message: '请选择策略' }]}>
            <Select options={strategies.map((s) => ({ value: s.key, label: `${s.name}（${s.key}）` }))} />
          </Form.Item>
          <Form.Item
            name="version" label="版本号"
            rules={[{ required: true, message: '请输入版本号，如 v1.1' }]}
          >
            <Input placeholder="v1.1" />
          </Form.Item>
          <Form.Item name="remark" label="备注（本次调参原因）">
            <Input.TextArea rows={2} placeholder="如：弱市调高资金权重" />
          </Form.Item>
          <Form.Item name="activate" label="保存后立即激活" valuePropName="checked">
            <Switch checkedChildren="是" unCheckedChildren="否" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default SelectionConfig;
