import React, { useEffect, useState } from 'react';
import {
  Card, Table, Tag, message, Button, Modal, Form, Input, InputNumber,
  Switch, Space, Popconfirm, Tooltip, Row, Col, Select, Alert, Divider,
} from 'antd';
import {
  ApartmentOutlined, PlusOutlined, ReloadOutlined, EditOutlined,
  DeleteOutlined, CopyOutlined,
} from '@ant-design/icons';
import {
  listStrategyDefs, getStrategyDefBuiltins, createStrategyDef,
  updateStrategyDef, deleteStrategyDef, getSelectionPatterns,
} from '../api';
import type { StrategyDefItem, StrategyDefinition, PatternInfo } from '../api';

const SEC_TITLE: React.CSSProperties = {
  fontWeight: 600, color: '#1677ff', margin: '4px 0 16px',
  borderLeft: '3px solid #1677ff', paddingLeft: 8,
};

const FILTER_FIELDS: { key: keyof StrategyDefinition['filters']; label: string; tip: string }[] = [
  { key: 'byRsi', label: 'RSI 超买过滤', tip: '按 MaxRsi 过滤超买（低吸/题材开，趋势关以容忍强势）' },
  { key: 'byRise20d', label: '20日追高过滤', tip: '按 MaxRise20d 过滤追高' },
  { key: 'byMinInflow', label: '主力净流入过滤', tip: '按 MinMainNetInflow 过滤资金' },
  { key: 'requireAboveMa20', label: '要求站上 MA20', tip: '趋势策略核心门槛' },
  { key: 'requireHotConcept', label: '要求命中热门题材', tip: '题材策略核心门槛' },
  { key: 'excludeTraditionalBigCap', label: '排除传统大盘股', tip: '排除传统低弹性行业 + 大市值' },
  { key: 'weakRegimeTightenRise20d', label: '弱市收紧追高', tip: '弱市把 MaxRise20d 收紧到 30' },
  { key: 'weakRegimeRequireInflow', label: '弱市要求净流入>0', tip: '弱市要求主力净流入为正' },
];

/** 空白定义模板（新建默认值，等价低吸口径） */
const emptyDefinition = (): StrategyDefinition => ({
  key: '', name: '', description: '', preferredRegime: '',
  filters: {
    byRsi: true, byRise20d: true, byMinInflow: true,
    requireAboveMa20: false, requireHotConcept: false,
    excludeTraditionalBigCap: true, extremeRise20d: null,
    weakRegimeTightenRise20d: true, weakRegimeRequireInflow: true,
    requirePatterns: [], requireAllPatterns: false,
  },
  factorKinds: { technical: 'lowdip', position: 'lowdip' },
  penalty: 'limitup', coreLogicTemplate: null, scanFullUniverse: false,
});

type FormShape = StrategyDefinition & { enabled: boolean };

/** 策略管理 — 自建选股策略的"口径定义"CRUD（过滤口径 + 因子口径 + 惩罚口径） */
const StrategyManager: React.FC = () => {
  const [form] = Form.useForm<FormShape>();
  const [list, setList] = useState<StrategyDefItem[]>([]);
  const [builtins, setBuiltins] = useState<StrategyDefinition[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [patterns, setPatterns] = useState<PatternInfo[]>([]);

  useEffect(() => {
    loadList();
    getStrategyDefBuiltins().then((r) => setBuiltins(r.data || [])).catch(() => {});
    getSelectionPatterns().then((r) => setPatterns(r.data || [])).catch(() => {});
  }, []);

  const loadList = async () => {
    setLoading(true);
    try {
      const res = await listStrategyDefs();
      setList(res.data || []);
    } catch {
      message.error('加载自建策略失败');
    } finally {
      setLoading(false);
    }
  };

  const openCreate = () => {
    setEditingId(null);
    form.setFieldsValue({ ...emptyDefinition(), enabled: true });
    setModalOpen(true);
  };

  const openEdit = (row: StrategyDefItem) => {
    setEditingId(row.id);
    form.setFieldsValue({ ...row.definition, enabled: row.enabled });
    setModalOpen(true);
  };

  const cloneFromBuiltin = (key: string) => {
    const tpl = builtins.find((b) => b.key === key);
    if (!tpl) return;
    // 克隆口径，但清空 key（须新起一个不与内置冲突的 key）并改名
    form.setFieldsValue({
      ...tpl,
      key: '',
      name: `${tpl.name}副本`,
      enabled: true,
    });
    message.info(`已套用「${tpl.name}」口径，请改 key/名称后保存`);
  };

  const doSave = async () => {
    try {
      const v = await form.validateFields();
      const { enabled, ...definition } = v;
      setSaving(true);
      const body = { enabled, definition: definition as StrategyDefinition };
      if (editingId == null) await createStrategyDef(body);
      else await updateStrategyDef(editingId, body);
      message.success(editingId == null ? '已创建' : '已更新');
      setModalOpen(false);
      await loadList();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data || '保存失败');
    } finally {
      setSaving(false);
    }
  };

  const remove = async (id: number) => {
    try {
      await deleteStrategyDef(id);
      message.success('已删除');
      await loadList();
    } catch (e: any) {
      message.error(e?.response?.data || '删除失败');
    }
  };

  // 自建 + 内置（内置只读，标 Tag）合并展示
  const rows = [
    ...builtins.map((b, i) => ({
      key: `builtin-${b.key}`, id: -1 - i, builtin: true,
      enabled: true, definition: b, updatedAt: '',
    })),
    ...list.map((r) => ({ key: `def-${r.id}`, builtin: false, ...r })),
  ];

  const columns = [
    {
      title: 'Key', dataIndex: ['definition', 'key'], width: 130,
      render: (k: string, row: any) => (
        <Space>
          <code>{k}</code>
          {row.builtin && <Tag color="gold">内置</Tag>}
        </Space>
      ),
    },
    { title: '名称', dataIndex: ['definition', 'name'], width: 130 },
    { title: '说明', dataIndex: ['definition', 'description'], ellipsis: true },
    { title: '适用环境', dataIndex: ['definition', 'preferredRegime'], width: 140 },
    {
      title: '因子口径', width: 150,
      render: (_: unknown, row: any) => (
        <Space size={4}>
          <Tag>技{row.definition.factorKinds?.technical}</Tag>
          <Tag>位{row.definition.factorKinds?.position}</Tag>
        </Space>
      ),
    },
    {
      title: '惩罚', dataIndex: ['definition', 'penalty'], width: 90,
      render: (p: string) => <Tag color={p === 'limitup' ? 'volcano' : 'default'}>{p}</Tag>,
    },
    {
      title: '状态', dataIndex: 'enabled', width: 90,
      render: (v: boolean, row: any) =>
        row.builtin ? <Tag color="blue">代码内置</Tag>
          : v ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>,
    },
    {
      title: '操作', width: 130,
      render: (_: unknown, row: any) =>
        row.builtin ? (
          <Tooltip title="以此内置口径克隆新策略">
            <Button size="small" icon={<CopyOutlined />} onClick={() => cloneFromBuiltin(row.definition.key)}>克隆</Button>
          </Tooltip>
        ) : (
          <Space size="small">
            <Tooltip title="编辑"><Button size="small" icon={<EditOutlined />} onClick={() => openEdit(row)} /></Tooltip>
            <Popconfirm title="删除此策略？" onConfirm={() => remove(row.id)} okText="删除" cancelText="取消">
              <Tooltip title="删除"><Button size="small" danger icon={<DeleteOutlined />} /></Tooltip>
            </Popconfirm>
          </Space>
        ),
    },
  ];

  return (
    <div>
      <h2><ApartmentOutlined /> 策略管理</h2>
      <Alert
        type="info"
        style={{ marginBottom: 16 }}
        showIcon
        message="自建策略只定义「口径」（过滤开关 + 因子口径 + 惩罚），不含权重/阈值。"
        description="新建后流程：① 在此定义口径 → ② 到「选股配置」为该策略 key 存权重/阈值版本 → ③ 到「选股回测」用该策略验证有效性 → ④ 激活配置后用于选股。内置策略由代码执行，仅可克隆。"
      />

      <Card
        title="策略清单（内置 + 自建）"
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} onClick={loadList}>刷新</Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>新建策略</Button>
          </Space>
        }
      >
        <Table
          columns={columns as any}
          dataSource={rows}
          rowKey="key"
          loading={loading}
          pagination={false}
          size="small"
        />
      </Card>

      <Modal
        title={editingId == null ? '新建自建策略' : '编辑自建策略'}
        open={modalOpen}
        onOk={doSave}
        onCancel={() => setModalOpen(false)}
        confirmLoading={saving}
        okText="保存"
        cancelText="取消"
        width={760}
        destroyOnClose
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          {editingId == null && builtins.length > 0 && (
            <Alert
              type="info"
              style={{ marginBottom: 12 }}
              message={
                <Space>
                  以内置为模板：
                  {builtins.map((b) => (
                    <Button key={b.key} size="small" icon={<CopyOutlined />} onClick={() => cloneFromBuiltin(b.key)}>
                      {b.name}
                    </Button>
                  ))}
                </Space>
              }
            />
          )}

          <div style={SEC_TITLE}>基本信息</div>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item
                name="key" label="策略 Key（唯一）"
                rules={[
                  { required: true, message: '必填' },
                  { pattern: /^[a-z][a-z0-9_]{1,49}$/, message: '小写字母开头，仅小写字母/数字/下划线，2-50位' },
                ]}
              >
                <Input placeholder="如 lowdip_v3" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="name" label="展示名" rules={[{ required: true, message: '必填' }]}>
                <Input placeholder="如 低吸加强版" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="preferredRegime" label="适用大盘环境">
                <Input placeholder="如 弱市 / 震荡市" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="description" label="一句话说明">
            <Input.TextArea rows={2} placeholder="说明该策略偏好什么品种/场景" />
          </Form.Item>

          <div style={SEC_TITLE}>因子口径 / 惩罚</div>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name={['factorKinds', 'technical']} label="技术因子口径" rules={[{ required: true }]}>
                <Select
                  options={[
                    { value: 'lowdip', label: 'lowdip（RSI超买不加分）' },
                    { value: 'trend', label: 'trend（容忍高RSI、均线多头）' },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name={['factorKinds', 'position']} label="位置因子口径" rules={[{ required: true }]}>
                <Select
                  options={[
                    { value: 'lowdip', label: 'lowdip（越低越好）' },
                    { value: 'trend', label: 'trend（中段甜区）' },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="penalty" label="惩罚口径" rules={[{ required: true }]}>
                <Select
                  options={[
                    { value: 'limitup', label: 'limitup（涨停/连板追高惩罚）' },
                    { value: 'none', label: 'none（不惩罚）' },
                  ]}
                />
              </Form.Item>
            </Col>
          </Row>

          <div style={SEC_TITLE}>硬过滤开关</div>
          <Row gutter={16}>
            {FILTER_FIELDS.map((f) => (
              <Col span={8} key={f.key}>
                <Form.Item
                  name={['filters', f.key]} label={<Tooltip title={f.tip}>{f.label}</Tooltip>}
                  valuePropName="checked"
                >
                  <Switch checkedChildren="开" unCheckedChildren="关" />
                </Form.Item>
              </Col>
            ))}
            <Col span={8}>
              <Form.Item
                name={['filters', 'extremeRise20d']}
                label={<Tooltip title="20日涨幅极端硬顶(%)，留空不限；趋势用较大值如100只挡极端透支">极端追高硬顶(%)</Tooltip>}
              >
                <InputNumber style={{ width: '100%' }} min={0} max={500} step={5} placeholder="留空=不限" />
              </Form.Item>
            </Col>
          </Row>

          <div style={SEC_TITLE}>K线形态过滤（可与上面的因子过滤组合）</div>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item
                name={['filters', 'requirePatterns']}
                label={<Tooltip title="要求命中的 K 线形态；留空=不做形态过滤。选了即对该策略启用形态硬过滤">要求命中的形态（留空=不启用）</Tooltip>}
              >
                <Select
                  mode="multiple"
                  allowClear
                  placeholder="选择要命中的形态"
                  options={patterns.map((p) => ({ value: p.key, label: p.name }))}
                  maxTagCount="responsive"
                />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item
                name={['filters', 'requireAllPatterns']}
                label={<Tooltip title="开=必须同时命中所选全部形态；关=命中任一即可">命中要求</Tooltip>}
                valuePropName="checked"
              >
                <Switch checkedChildren="全部命中" unCheckedChildren="命中任一" />
              </Form.Item>
            </Col>
            <Col span={6}>
              <Form.Item
                name="scanFullUniverse"
                label={<Tooltip title="开=跳过活跃度粗筛、扫描全市场（形态策略建议开，形态可能出现在非活跃股）">全市场扫描</Tooltip>}
                valuePropName="checked"
              >
                <Switch checkedChildren="全市场" unCheckedChildren="活跃池" />
              </Form.Item>
            </Col>
          </Row>

          <Divider style={{ margin: '4px 0 16px' }} />
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="coreLogicTemplate" label="核心逻辑文案模板（留空用通用模板）">
                <Input placeholder="如 低吸加强版：左侧埋伏，低位温和放量" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="enabled" label="启用（停用则不进策略池）" valuePropName="checked">
                <Switch checkedChildren="启用" unCheckedChildren="停用" />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>
    </div>
  );
};

export default StrategyManager;
