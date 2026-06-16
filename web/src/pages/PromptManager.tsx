import React, { useEffect, useState } from 'react';
import { Card, Table, Tag, message, Button, Modal, Space, Form, Input, Select, InputNumber, Popconfirm } from 'antd';
import { FileTextOutlined, ReloadOutlined, EyeOutlined, EditOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { getPrompts, getPromptDetail, savePrompt, deletePrompt, reloadPrompts } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import type { PromptInfo, PromptTemplate } from '../types/models';

const { TextArea } = Input;

const categoryColors: Record<string, string> = {
  technical: 'blue',
  macro: 'purple',
  sentiment: 'orange',
  strategy: 'green',
  risk: 'red',
};

const categoryOptions = [
  { label: '技术分析', value: 'technical' },
  { label: '宏观', value: 'macro' },
  { label: '情绪', value: 'sentiment' },
  { label: '策略', value: 'strategy' },
  { label: '风控', value: 'risk' },
];

const defaultTemplate: PromptTemplate = {
  name: '',
  version: 'v1',
  category: 'strategy',
  description: '',
  model: '',
  temperature: 0.3,
  maxTokens: 2000,
  variables: [],
  systemPrompt: '',
  userPrompt: '',
};

/** Prompt 模板管理 — 完整 CRUD */
const PromptManager: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [prompts, setPrompts] = useState<PromptInfo[]>([]);
  const [editorOpen, setEditorOpen] = useState(false);
  const [detailOpen, setDetailOpen] = useState(false);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [detail, setDetail] = useState<PromptTemplate | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [editForm] = Form.useForm<PromptTemplate>();

  useEffect(() => { loadPrompts(); }, []);

  const loadPrompts = async () => {
    setLoading(true);
    try {
      const res = await getPrompts();
      setPrompts(res.data || []);
    } catch {
      message.error('加载Prompt列表失败');
    } finally {
      setLoading(false);
    }
  };

  const handleReload = async () => {
    try {
      await reloadPrompts();
      message.success('缓存已刷新');
      await loadPrompts();
    } catch {
      message.error('刷新失败');
    }
  };

  const handleView = async (record: PromptInfo) => {
    setDetailLoading(true);
    setDetailOpen(true);
    try {
      const res = await getPromptDetail(record.name, record.version);
      setDetail(res.data);
    } catch {
      message.error('加载详情失败');
      setDetailOpen(false);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleCreate = () => {
    setEditing(false);
    editForm.setFieldsValue({ ...defaultTemplate });
    setEditorOpen(true);
  };

  const handleEdit = async (record: PromptInfo) => {
    setEditing(true);
    try {
      const res = await getPromptDetail(record.name, record.version);
      editForm.setFieldsValue(res.data);
      setEditorOpen(true);
    } catch {
      message.error('加载编辑数据失败');
    }
  };

  const handleDelete = async (record: PromptInfo) => {
    try {
      await deletePrompt(record.name, record.version);
      message.success(`已删除 ${record.name}:${record.version}`);
      await loadPrompts();
    } catch {
      message.error('删除失败');
    }
  };

  const handleSave = async () => {
    try {
      const values = await editForm.validateFields();
      setSaving(true);
      await savePrompt(values as PromptTemplate);
      message.success(editing ? '更新成功' : '创建成功');
      setEditorOpen(false);
      await loadPrompts();
    } catch {
      // validation error or API error
    } finally {
      setSaving(false);
    }
  };

  const columns = [
    { title: '名称', dataIndex: 'name', key: 'name', ellipsis: true },
    { title: '版本', dataIndex: 'version', key: 'version', width: 70 },
    {
      title: '分类', dataIndex: 'category', key: 'category', width: 100,
      render: (v: string) => <Tag color={categoryColors[v] || 'default'}>{v}</Tag>,
    },
    { title: '描述', dataIndex: 'description', key: 'description', ellipsis: true },
    {
      title: '操作', key: 'actions', width: 180,
      render: (_: unknown, record: PromptInfo) => (
        <Space size="small">
          <Button size="small" icon={<EyeOutlined />} onClick={() => handleView(record)}>
            查看
          </Button>
          <Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确定删除此Prompt？"
            description="删除后无法恢复"
            onConfirm={() => handleDelete(record)}
            okText="删除"
            cancelText="取消"
            okButtonProps={{ danger: true }}
          >
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  if (loading) return <LoadingSkeleton />;

  return (
    <div>
      <h2>
        <FileTextOutlined /> Prompt 管理
        <Button
          type="primary"
          size="small"
          icon={<PlusOutlined />}
          style={{ marginLeft: 12 }}
          onClick={handleCreate}
        >
          新建
        </Button>
        <Button
          icon={<ReloadOutlined />}
          size="small"
          style={{ marginLeft: 8 }}
          onClick={handleReload}
        >
          刷新缓存
        </Button>
      </h2>

      <Card>
        <Table
          columns={columns}
          dataSource={prompts}
          rowKey={(r) => `${r.name}:${r.version}`}
          pagination={false}
          scroll={{ x: 750 }}
        />
      </Card>

      {/* 查看详情 Modal */}
      <Modal
        title={`Prompt 详情 — ${detail?.name}:${detail?.version}`}
        open={detailOpen}
        onCancel={() => setDetailOpen(false)}
        footer={null}
        width="min(700px, 94vw)"
        loading={detailLoading}
      >
        {detail && (
          <Space direction="vertical" style={{ width: '100%' }} size="middle">
            <div>
              <span style={{ marginRight: 8 }}>分类:</span>
              <Tag color={categoryColors[detail.category] || 'default'}>{detail.category}</Tag>
              <span style={{ marginLeft: 16, marginRight: 8 }}>模型:</span>
              <Tag>{detail.model || '-'}</Tag>
              <span style={{ marginLeft: 16, marginRight: 8 }}>温度:</span>
              <Tag>{detail.temperature}</Tag>
              <span style={{ marginLeft: 16, marginRight: 8 }}>MaxTokens:</span>
              <Tag>{detail.maxTokens ?? '默认'}</Tag>
            </div>
            {detail.description && <div><strong>描述:</strong> {detail.description}</div>}
            {detail.variables?.length > 0 && (
              <div>
                <strong>变量: </strong>
                {detail.variables.map((v) => <Tag key={v}>{`{${v}}`}</Tag>)}
              </div>
            )}
            {detail.systemPrompt && (
              <Card title="System Prompt" size="small">
                <pre style={{ whiteSpace: 'pre-wrap', margin: 0, fontSize: 13, background: '#f5f5f5', padding: 12, borderRadius: 4 }}>
                  {detail.systemPrompt}
                </pre>
              </Card>
            )}
            <Card title="User Prompt" size="small">
              <pre style={{ whiteSpace: 'pre-wrap', margin: 0, fontSize: 13, background: '#f5f5f5', padding: 12, borderRadius: 4 }}>
                {detail.userPrompt}
              </pre>
            </Card>
          </Space>
        )}
      </Modal>

      {/* 编辑/新增 Modal */}
      <Modal
        title={editing ? '编辑 Prompt' : '新建 Prompt'}
        open={editorOpen}
        onOk={handleSave}
        onCancel={() => setEditorOpen(false)}
        width="min(750px, 94vw)"
        confirmLoading={saving}
        destroyOnClose
      >
        <Form<PromptTemplate> form={editForm} layout="vertical" initialValues={defaultTemplate}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 16px' }}>
            <Form.Item name="name" label="名称" rules={[{ required: true, message: '请输入名称' }]}>
              <Input placeholder="如 trend-analysis" disabled={editing} />
            </Form.Item>
            <Form.Item name="version" label="版本" rules={[{ required: true, message: '请输入版本' }]} initialValue="v1">
              <Input placeholder="如 v1, v2" />
            </Form.Item>
            <Form.Item name="category" label="分类" rules={[{ required: true }]}>
              <Select options={categoryOptions} />
            </Form.Item>
            <Form.Item name="model" label="推荐模型">
              <Input placeholder="模型ID，如 deepseek-v3" />
            </Form.Item>
            <Form.Item name="temperature" label="温度">
              <InputNumber min={0} max={2} step={0.1} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="maxTokens" label="MaxTokens">
              <InputNumber min={1} max={32768} style={{ width: '100%' }} placeholder="留空使用默认" />
            </Form.Item>
          </div>
          <Form.Item name="description" label="描述">
            <Input placeholder="Prompt用途简介" />
          </Form.Item>
          <Form.Item
            name="variables"
            label="变量列表"
            getValueFromEvent={(val: string[]) => val}
          >
            <Select
              mode="tags"
              placeholder="输入变量名后回车，如 code, rsi"
              style={{ width: '100%' }}
              tokenSeparators={[',']}
            />
          </Form.Item>
          <Form.Item name="systemPrompt" label="System Prompt">
            <TextArea rows={4} placeholder="系统提示词，定义角色和行为规则" />
          </Form.Item>
          <Form.Item
            name="userPrompt"
            label="User Prompt"
            rules={[{ required: true, message: 'User Prompt 不能为空' }]}
          >
            <TextArea rows={5} placeholder="用户提示词，使用 {variable} 作为占位符" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default PromptManager;
