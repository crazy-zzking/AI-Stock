import React, { useEffect, useState } from 'react';
import {
  Card, Table, Tag, Row, Col, Statistic, message, Button, Modal, Form,
  Input, InputNumber, Switch, Space, Popconfirm, Tooltip, Divider,
} from 'antd';
import {
  RobotOutlined, CheckCircleOutlined, CloseCircleOutlined,
  PlusOutlined, EditOutlined, DeleteOutlined, ReloadOutlined, BulbOutlined,
} from '@ant-design/icons';

import { getLLMModels, addLLMModel, updateLLMModel, deleteLLMModel, refreshLLMModels } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import type { LLMModel } from '../types/models';

/** LLM 模型管理 — 完整 CRUD */
const LLMManager: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [models, setModels] = useState<LLMModel[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingModel, setEditingModel] = useState<LLMModel | null>(null);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm();
  const watchedBaseUrl: string = Form.useWatch('baseUrl', form) ?? '';
  const isDeepSeek = watchedBaseUrl.includes('api.deepseek.com');

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
    });
    setModalOpen(true);
  };

  const openEditModal = (model: LLMModel) => {
    setEditingModel(model);
    form.setFieldsValue(model);
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

  const columns = [
    { title: 'ID', dataIndex: 'id', key: 'id', width: 100 },
    { title: '名称', dataIndex: 'name', key: 'name', ellipsis: true },
    { title: '模型', dataIndex: 'model', key: 'model' },
    {
      title: '状态', dataIndex: 'isEnabled', key: 'isEnabled', width: 90,
      render: (v: boolean) => (
        <Tag icon={v ? <CheckCircleOutlined /> : <CloseCircleOutlined />} color={v ? 'green' : 'default'}>
          {v ? '启用' : '禁用'}
        </Tag>
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
      title: '思考模式', dataIndex: 'enableThinking', key: 'enableThinking', width: 90,
      render: (v: boolean, record: LLMModel) =>
        record.baseUrl?.includes('api.deepseek.com') ? (
          <Tag icon={<BulbOutlined />} color={v ? 'gold' : 'default'}>
            {v ? '开启' : '关闭'}
          </Tag>
        ) : null,
    },
    {
      title: '操作', key: 'actions', width: 120,
      render: (_: unknown, record: LLMModel) => (
        <Space size="small">
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
        <Col span={6}>
          <Card>
            <Statistic title="模型总数" value={models.length} prefix={<RobotOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="已启用"
              value={models.filter((m) => m.isEnabled).length}
              valueStyle={{ color: '#3f8600' }}
              prefix={<CheckCircleOutlined />}
            />
          </Card>
        </Col>
        <Col span={6}>
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
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        confirmLoading={saving}
        okText="保存"
        cancelText="取消"
        width={600}
        destroyOnClose
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
            <Col span={12}>
              <Form.Item name="baseUrl" label="API 地址" rules={[{ required: true, message: '请输入API地址' }]}>
                <Input placeholder="https://api.openai.com/v1" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="model" label="模型标识" rules={[{ required: true, message: '请输入模型标识' }]}>
                <Input placeholder="gpt-4-turbo" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="apiKey" label="API Key" rules={[{ required: true, message: '请输入API Key' }]}>
            <Input.Password placeholder="sk-..." />
          </Form.Item>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="isEnabled" label="启用" valuePropName="checked">
                <Switch checkedChildren="启用" unCheckedChildren="禁用" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="priority" label="优先级" rules={[{ required: true }]}>
                <InputNumber min={0} max={100} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="timeoutSeconds" label="超时(秒)" rules={[{ required: true }]}>
                <InputNumber min={5} max={300} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="maxTokens" label="最大 Token">
                <InputNumber min={1} max={200000} style={{ width: '100%' }} placeholder="不限" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="temperature" label="温度">
                <InputNumber min={0} max={2} step={0.1} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="description" label="描述">
            <Input.TextArea rows={2} placeholder="模型用途说明（可选）" />
          </Form.Item>
          {isDeepSeek && (
            <>
              <Divider style={{ fontSize: 13 }}>
                <BulbOutlined /> DeepSeek 思考模式
              </Divider>
              <Row gutter={16}>
                <Col span={8}>
                  <Form.Item name="enableThinking" label="开启思考" valuePropName="checked">
                    <Switch checkedChildren="开" unCheckedChildren="关" />
                  </Form.Item>
                </Col>
                <Col span={16}>
                  <Form.Item
                    name="thinkingBudgetTokens"
                    label="思考 Token 预算"
                    extra="最小 1000，不填默认 8000"
                  >
                    <InputNumber min={1000} max={32000} step={1000} style={{ width: '100%' }} placeholder="8000" />
                  </Form.Item>
                </Col>
              </Row>
            </>
          )}
        </Form>
      </Modal>
    </div>
  );
};

export default LLMManager;
