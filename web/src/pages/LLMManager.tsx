import React, { useEffect, useState } from 'react';
import {
  Card, Table, Tag, Row, Col, Statistic, message, Button, Modal, Form,
  Input, InputNumber, Switch, Space, Popconfirm, Tooltip, Divider, Spin, Alert, Descriptions, Select,
} from 'antd';
import {
  RobotOutlined, CheckCircleOutlined, CloseCircleOutlined,
  PlusOutlined, EditOutlined, DeleteOutlined, ReloadOutlined, BulbOutlined, WalletOutlined, PictureOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';

import {
  getLLMModels, addLLMModel, updateLLMModel, deleteLLMModel, refreshLLMModels, getDeepSeekBalance, testLLMModel, testLLMConfig,
} from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import type { LLMModel, DeepSeekBalance, LLMTestResponse } from '../types/models';

/** 思考参数格式选项 */
const THINKING_FORMAT_OPTIONS = [
  { value: 'deepseek', label: 'DeepSeek（thinking）' },
  { value: 'qwen', label: 'Qwen（enable_thinking）' },
  { value: 'reasoning_effort', label: 'reasoning_effort（OpenAI o 系等）' },
];
const THINKING_FORMAT_LABELS: Record<string, string> = {
  deepseek: 'DeepSeek',
  qwen: 'Qwen',
  reasoning_effort: 'effort',
};

/** LLM 模型管理 — 完整 CRUD */
const LLMManager: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [models, setModels] = useState<LLMModel[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingModel, setEditingModel] = useState<LLMModel | null>(null);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm();
  const watchedEnableThinking: boolean = Form.useWatch('enableThinking', form) ?? false;
  const watchedThinkingFormat: string = Form.useWatch('thinkingFormat', form) ?? 'deepseek';

  useEffect(() => { loadModels(); }, []);

  const loadModels = async () => {
    setLoading(true);
    try {
      const res = await getLLMModels();
      setModels(res.data || []);
    } catch {
      message.error('加载模型列表失败');
    } finally {
      setLoading(false);
    }
  };

  const handleRefresh = async () => {
    try {
      await refreshLLMModels();
      message.success('缓存已刷新');
      await loadModels();
    } catch {
      message.error('刷新失败');
    }
  };

  const openAddModal = () => {
    setEditingModel(null);
    form.resetFields();
    form.setFieldsValue({
      isEnabled: true,
      priority: 0,
      timeoutSeconds: 30,
      temperature: 0.7,
      thinkingFormat: 'deepseek',
    });
    setEditTest({ running: false, result: null });
    setModalOpen(true);
  };

  const openEditModal = (model: LLMModel) => {
    setEditingModel(model);
    form.setFieldsValue({ ...model, thinkingFormat: model.thinkingFormat || 'deepseek' });
    setEditTest({ running: false, result: null });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      setSaving(true);
      const data: LLMModel = { ...values };
      if (editingModel) {
        data.id = editingModel.id;
        await updateLLMModel(editingModel.id, data);
        message.success('模型已更新');
      } else {
        await addLLMModel(data);
        message.success('模型已添加');
      }
      setModalOpen(false);
      await loadModels();
    } catch (err: any) {
      if (err?.errorFields) return; // form validation
      message.error(err?.response?.data?.error || '操作失败');
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (modelId: string) => {
    try {
      await deleteLLMModel(modelId);
      message.success('模型已删除');
      await loadModels();
    } catch {
      message.error('删除失败');
    }
  };

  // 列表直接切换启用/禁用，不用进编辑弹窗
  const toggleEnabled = async (record: LLMModel, checked: boolean) => {
    try {
      await updateLLMModel(record.id, { ...record, isEnabled: checked });
      setModels((prev) => prev.map((m) => (m.id === record.id ? { ...m, isEnabled: checked } : m)));
      message.success(checked ? '已启用' : '已禁用');
    } catch {
      message.error('操作失败');
    }
  };

  // DeepSeek 余额（实时查询，不入库）
  const [balanceState, setBalanceState] = useState<{
    open: boolean; loading: boolean; data: DeepSeekBalance | null; name: string;
  }>({ open: false, loading: false, data: null, name: '' });

  const handleQueryBalance = async (record: LLMModel) => {
    setBalanceState({ open: true, loading: true, data: null, name: record.name });
    try {
      const res = await getDeepSeekBalance(record.id);
      setBalanceState((s) => ({ ...s, loading: false, data: res.data }));
    } catch {
      setBalanceState((s) => ({
        ...s, loading: false,
        data: { success: false, isAvailable: false, balanceInfos: [], errorMessage: '请求失败' },
      }));
    }
  };

  // 模型连通性测试（真实调用外部 API，含未启用模型）
  const DEFAULT_TEST_PROMPT = '你好，请用一句话简单自我介绍。';
  const [testState, setTestState] = useState<{
    open: boolean; running: boolean; modelId: string; name: string;
    prompt: string; result: LLMTestResponse | null;
  }>({ open: false, running: false, modelId: '', name: '', prompt: DEFAULT_TEST_PROMPT, result: null });

  const openTestModal = (record: LLMModel) => {
    setTestState({
      open: true, running: false, modelId: record.id, name: record.name,
      prompt: DEFAULT_TEST_PROMPT, result: null,
    });
  };

  const handleRunTest = async () => {
    if (!testState.prompt.trim()) {
      message.warning('请输入测试提示词');
      return;
    }
    setTestState((s) => ({ ...s, running: true, result: null }));
    try {
      const res = await testLLMModel(testState.modelId, testState.prompt);
      setTestState((s) => ({ ...s, running: false, result: res.data }));
    } catch (err: any) {
      setTestState((s) => ({
        ...s, running: false,
        result: {
          success: false, content: '', modelId: s.modelId, modelName: s.name,
          responseTimeMs: 0, errorMessage: err?.response?.data?.error || '请求失败',
        },
      }));
    }
  };

  // 编辑/新建弹窗内联测试：用表单当前值（含未保存修改），不读库
  const [editTest, setEditTest] = useState<{ running: boolean; result: LLMTestResponse | null }>(
    { running: false, result: null },
  );

  const handleEditTest = async () => {
    let values: LLMModel;
    try {
      values = await form.validateFields();
    } catch {
      return; // 表单校验未过
    }
    setEditTest({ running: true, result: null });
    try {
      const res = await testLLMConfig(values);
      setEditTest({ running: false, result: res.data });
    } catch (err: any) {
      setEditTest({
        running: false,
        result: {
          success: false, content: '', modelId: values.id, modelName: values.name,
          responseTimeMs: 0, errorMessage: err?.response?.data?.error || '请求失败',
        },
      });
    }
  };

  // 测试结果展示（编辑弹窗与独立测试弹窗共用）
  const renderTestResult = (result: LLMTestResponse) => (
    result.success ? (
      <>
        <Alert
          type="success"
          showIcon
          message={`调用成功 · 耗时 ${result.responseTimeMs} ms`}
          style={{ marginBottom: 12 }}
        />
        {result.thinkingContent && (
          <Descriptions bordered size="small" column={1} style={{ marginBottom: 12 }}>
            <Descriptions.Item label="思考过程">
              <div style={{ whiteSpace: 'pre-wrap', color: '#999' }}>{result.thinkingContent}</div>
            </Descriptions.Item>
          </Descriptions>
        )}
        <Descriptions bordered size="small" column={1}>
          <Descriptions.Item label="响应内容">
            <div style={{ whiteSpace: 'pre-wrap' }}>{result.content}</div>
          </Descriptions.Item>
          <Descriptions.Item label="实际模型">{result.modelName || result.modelId}</Descriptions.Item>
          {result.usage && (
            <Descriptions.Item label="Token 用量">
              输入 {result.usage.promptTokens} · 输出 {result.usage.completionTokens} · 合计 {result.usage.totalTokens}
            </Descriptions.Item>
          )}
        </Descriptions>
      </>
    ) : (
      <Alert type="error" showIcon message="调用失败" description={result.errorMessage} />
    )
  );

  const columns = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 100 },
    { title: '名称', dataIndex: 'name', key: 'name', ellipsis: true },
    { title: '模型', dataIndex: 'model', key: 'model' },
    {
      title: '状态', dataIndex: 'isEnabled', key: 'isEnabled', width: 90,
      render: (v: boolean, record: LLMModel) => (
        <Switch
          size="small"
          checked={v}
          checkedChildren="启用"
          unCheckedChildren="禁用"
          onChange={(checked) => toggleEnabled(record, checked)}
        />
      ),
    },
    { title: '优先级', dataIndex: 'priority', key: 'priority', width: 70 },
    { title: '超时(s)', dataIndex: 'timeoutSeconds', key: 'timeoutSeconds', width: 80 },
    {
      title: '温度', dataIndex: 'temperature', key: 'temperature', width: 70,
      render: (v: number) => v?.toFixed(2),
    },
    { title: '描述', dataIndex: 'description', key: 'description', ellipsis: true },
    {
      title: '思考模式', dataIndex: 'enableThinking', key: 'enableThinking', width: 130,
      render: (v: boolean, record: LLMModel) =>
        v ? (
          <Tag icon={<BulbOutlined />} color="gold">
            {THINKING_FORMAT_LABELS[record.thinkingFormat || 'deepseek'] || record.thinkingFormat}
          </Tag>
        ) : (
          <Tag color="default">关闭</Tag>
        ),
    },
    {
      title: '多模态', dataIndex: 'supportsMultimodal', key: 'supportsMultimodal', width: 90,
      render: (v: boolean) => (
        <Tag icon={<PictureOutlined />} color={v ? 'green' : 'default'}>
          {v ? '支持' : '不支持'}
        </Tag>
      ),
    },
    {
      title: '操作', key: 'actions', width: 200,
      render: (_: unknown, record: LLMModel) => (
        <Space size="small">
          <Tooltip title="测试连通性">
            <Button size="small" icon={<ThunderboltOutlined />} onClick={() => openTestModal(record)} />
          </Tooltip>
          {record.baseUrl?.includes('api.deepseek.com') && (
            <Tooltip title="查余额">
              <Button size="small" icon={<WalletOutlined />} onClick={() => handleQueryBalance(record)} />
            </Tooltip>
          )}
          <Tooltip title="编辑">
            <Button size="small" icon={<EditOutlined />} onClick={() => openEditModal(record)} />
          </Tooltip>
          <Popconfirm
            title="确定删除此模型？"
            description="删除后不可恢复"
            onConfirm={() => handleDelete(record.id)}
            okText="确定"
            cancelText="取消"
          >
            <Tooltip title="删除">
              <Button size="small" danger icon={<DeleteOutlined />} />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  if (loading) return <LoadingSkeleton />;

  return (
    <div>
      <h2>
        <RobotOutlined /> LLM 模型管理
        <Button
          icon={<ReloadOutlined />}
          size="small"
          style={{ marginLeft: 12 }}
          onClick={handleRefresh}
        >
          刷新缓存
        </Button>
      </h2>

      {/* 概览 */}
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic title="模型总数" value={models.length} prefix={<RobotOutlined />} />
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic
              title="已启用"
              value={models.filter((m) => m.isEnabled).length}
              valueStyle={{ color: '#3f8600' }}
              prefix={<CheckCircleOutlined />}
            />
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic
              title="已禁用"
              value={models.filter((m) => !m.isEnabled).length}
              valueStyle={{ color: '#999' }}
              prefix={<CloseCircleOutlined />}
            />
          </Card>
        </Col>
      </Row>

      {/* 模型列表 */}
      <Card
        title="模型列表"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openAddModal}>
            新建模型
          </Button>
        }
      >
        <Table
          columns={columns}
          dataSource={models}
          rowKey="id"
          pagination={false}
          scroll={{ x: 900 }}
        />
      </Card>

      {/* 新建/编辑弹窗 */}
      <Modal
        title={editingModel ? '编辑模型' : '新建模型'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        width="min(600px, 94vw)"
        destroyOnClose
        footer={[
          <Button
            key="test"
            icon={<ThunderboltOutlined />}
            loading={editTest.running}
            onClick={handleEditTest}
            style={{ float: 'left' }}
          >
            测试
          </Button>,
          <Button key="cancel" onClick={() => setModalOpen(false)}>取消</Button>,
          <Button key="save" type="primary" loading={saving} onClick={handleSave}>保存</Button>,
        ]}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="id"
            label="模型 ID"
            rules={[
              { required: true, message: '请输入模型ID' },
              { pattern: /^[a-zA-Z0-9_-]+$/, message: '仅允许字母、数字、下划线和连字符' },
            ]}
          >
            <Input placeholder="如: gpt-4, deepseek-v3" disabled={!!editingModel} />
          </Form.Item>
          <Form.Item name="name" label="显示名称" rules={[{ required: true, message: '请输入显示名称' }]}>
            <Input placeholder="如: GPT-4 Turbo" />
          </Form.Item>
          <Row gutter={16}>
            <Col xs={24} sm={12}>
              <Form.Item name="baseUrl" label="API 地址" rules={[{ required: true, message: '请输入API地址' }]}>
                <Input placeholder="https://api.openai.com/v1" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="model" label="模型标识" rules={[{ required: true, message: '请输入模型标识' }]}>
                <Input placeholder="gpt-4-turbo" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="apiKey" label="API Key" rules={[{ required: true, message: '请输入API Key' }]}>
            <Input.Password placeholder="sk-..." />
          </Form.Item>
          <Row gutter={16}>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="isEnabled" label="启用" valuePropName="checked">
                <Switch checkedChildren="启用" unCheckedChildren="禁用" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="priority" label="优先级" rules={[{ required: true }]}>
                <InputNumber min={0} max={100} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="timeoutSeconds" label="超时(秒)" rules={[{ required: true }]}>
                <InputNumber min={5} max={300} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col xs={24} sm={12}>
              <Form.Item name="maxTokens" label="最大 Token">
                <InputNumber min={1} max={200000} style={{ width: '100%' }} placeholder="不限" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="temperature" label="温度">
                <InputNumber min={0} max={2} step={0.1} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="description" label="描述">
            <Input.TextArea rows={2} placeholder="模型用途说明（可选）" />
          </Form.Item>
          <Form.Item
            name="supportsMultimodal"
            label="支持多模态"
            valuePropName="checked"
            extra="开启后该模型可用于图片识别（如知识星球图片消息）"
          >
            <Switch checkedChildren="支持" unCheckedChildren="不支持" />
          </Form.Item>
          <Divider style={{ fontSize: 13 }}>
            <BulbOutlined /> 思考模式
          </Divider>
          <Row gutter={16}>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="enableThinking" label="开启思考" valuePropName="checked">
                <Switch checkedChildren="开" unCheckedChildren="关" />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item
                name="thinkingFormat"
                label="参数格式"
                extra="按模型厂商选择思考参数的注入格式"
              >
                <Select options={THINKING_FORMAT_OPTIONS} disabled={!watchedEnableThinking} />
              </Form.Item>
            </Col>
          </Row>
          {watchedEnableThinking && watchedThinkingFormat !== 'reasoning_effort' && (
            <Form.Item
              name="thinkingBudgetTokens"
              label="思考 Token 预算"
              extra="最小 1000，不填默认 8000"
            >
              <InputNumber min={1000} max={32000} step={1000} style={{ width: '100%' }} placeholder="8000" />
            </Form.Item>
          )}
          {watchedEnableThinking && watchedThinkingFormat === 'reasoning_effort' && (
            <Form.Item
              name="reasoningEffort"
              label="推理强度"
              extra="reasoning_effort 取值，不填默认 medium"
            >
              <Select
                placeholder="medium"
                allowClear
                options={[
                  { value: 'low', label: 'low' },
                  { value: 'medium', label: 'medium' },
                  { value: 'high', label: 'high' },
                ]}
              />
            </Form.Item>
          )}
        </Form>

        {/* 内联测试结果 */}
        {(editTest.running || editTest.result) && (
          <>
            <Divider style={{ fontSize: 13 }}><ThunderboltOutlined /> 测试结果</Divider>
            {editTest.running ? (
              <div style={{ textAlign: 'center', padding: 16 }}><Spin tip="模型响应中…" /></div>
            ) : (
              editTest.result && renderTestResult(editTest.result)
            )}
          </>
        )}
      </Modal>

      {/* DeepSeek 余额（实时查询，不入库） */}
      <Modal
        title={<><WalletOutlined /> DeepSeek 余额 — {balanceState.name}</>}
        open={balanceState.open}
        onCancel={() => setBalanceState((s) => ({ ...s, open: false }))}
        footer={null}
        width="min(480px, 94vw)"
        destroyOnClose
      >
        {balanceState.loading ? (
          <div style={{ textAlign: 'center', padding: 32 }}><Spin /></div>
        ) : !balanceState.data?.success ? (
          <Alert type="error" showIcon message="查询失败" description={balanceState.data?.errorMessage} />
        ) : (
          <>
            <Alert
              type={balanceState.data.isAvailable ? 'success' : 'warning'}
              showIcon
              message={balanceState.data.isAvailable ? '账户余额可用' : '账户余额不足，无法调用 API'}
              style={{ marginBottom: 16 }}
            />
            {balanceState.data.balanceInfos.length === 0 ? (
              <Alert type="info" message="无余额明细" />
            ) : (
              balanceState.data.balanceInfos.map((b) => (
                <Descriptions
                  key={b.currency}
                  bordered
                  size="small"
                  column={1}
                  title={`币种：${b.currency}`}
                  style={{ marginBottom: 12 }}
                >
                  <Descriptions.Item label="总可用余额">{b.totalBalance}</Descriptions.Item>
                  <Descriptions.Item label="赠金余额">{b.grantedBalance}</Descriptions.Item>
                  <Descriptions.Item label="充值余额">{b.toppedUpBalance}</Descriptions.Item>
                </Descriptions>
              ))
            )}
          </>
        )}
      </Modal>

      {/* 模型连通性测试（真实调用外部 API） */}
      <Modal
        title={<><ThunderboltOutlined /> 测试模型 — {testState.name}</>}
        open={testState.open}
        onOk={handleRunTest}
        onCancel={() => setTestState((s) => ({ ...s, open: false }))}
        confirmLoading={testState.running}
        okText="发送测试"
        cancelText="关闭"
        width="min(560px, 94vw)"
        destroyOnClose
      >
        <Alert
          type="warning"
          showIcon
          message="测试会真实调用外部 API 并消耗 Token"
          style={{ marginBottom: 12 }}
        />
        <Form layout="vertical">
          <Form.Item label="测试提示词">
            <Input.TextArea
              rows={3}
              value={testState.prompt}
              onChange={(e) => setTestState((s) => ({ ...s, prompt: e.target.value }))}
              placeholder="输入要发送给模型的提示词"
            />
          </Form.Item>
        </Form>

        {testState.running && (
          <div style={{ textAlign: 'center', padding: 24 }}><Spin tip="模型响应中…" /></div>
        )}

        {!testState.running && testState.result && renderTestResult(testState.result)}
      </Modal>
    </div>
  );
};

export default LLMManager;
