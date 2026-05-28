import React, { useEffect, useState } from 'react';
import { Card, Table, Button, Input, message, Tag, Space, Modal } from 'antd';
import { RobotOutlined, SendOutlined } from '@ant-design/icons';
import { getAgents, analyzeStock, generateSignal, makeDecision } from '../api';

const Agents: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [agents, setAgents] = useState<any[]>([]);
  const [code, setCode] = useState('');
  const [result, setResult] = useState<any>(null);
  const [modalVisible, setModalVisible] = useState(false);

  useEffect(() => {
    loadAgents();
  }, []);

  const loadAgents = async () => {
    try {
      const res = await getAgents();
      setAgents(res.data);
    } catch (error) {
      message.error('加载Agent失败');
    } finally {
      setLoading(false);
    }
  };

  const handleAnalyze = async () => {
    if (!code) {
      message.warning('请输入股票代码');
      return;
    }
    try {
      const res = await analyzeStock(code);
      setResult(res.data);
      setModalVisible(true);
    } catch (error) {
      message.error('分析失败');
    }
  };

  const handleSignal = async () => {
    if (!code) {
      message.warning('请输入股票代码');
      return;
    }
    try {
      const res = await generateSignal(code);
      setResult(res.data);
      setModalVisible(true);
    } catch (error) {
      message.error('信号生成失败');
    }
  };

  const handleDecision = async () => {
    if (!code) {
      message.warning('请输入股票代码');
      return;
    }
    try {
      const res = await makeDecision(code, 1000000);
      setResult(res.data);
      setModalVisible(true);
    } catch (error) {
      message.error('决策失败');
    }
  };

  const columns = [
    { title: 'Agent ID', dataIndex: 'agentId', key: 'agentId' },
    { title: '在线状态', dataIndex: 'isOnline', key: 'isOnline', render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? '在线' : '离线'}</Tag> },
    { title: '当前任务', dataIndex: 'currentTasks', key: 'currentTasks' },
    { title: '已完成', dataIndex: 'completedTasks', key: 'completedTasks' },
    { title: '最后活跃', dataIndex: 'lastActiveTime', key: 'lastActiveTime', render: (v: string) => new Date(v).toLocaleString() },
  ];

  return (
    <div>
      <h2>Agent监控</h2>
      <Card style={{ marginBottom: 16 }}>
        <Space>
          <Input 
            placeholder="股票代码" 
            value={code} 
            onChange={e => setCode(e.target.value)}
            style={{ width: 120 }}
          />
          <Button type="primary" icon={<SendOutlined />} onClick={handleAnalyze}>分析</Button>
          <Button icon={<SendOutlined />} onClick={handleSignal}>信号</Button>
          <Button icon={<SendOutlined />} onClick={handleDecision}>决策</Button>
        </Space>
      </Card>
      <Card title="Agent列表">
        <Table 
          columns={columns} 
          dataSource={agents} 
          rowKey="agentId"
          loading={loading}
          pagination={false}
        />
      </Card>
      <Modal
        title="执行结果"
        open={modalVisible}
        onCancel={() => setModalVisible(false)}
        footer={null}
        width={600}
      >
        <pre>{JSON.stringify(result, null, 2)}</pre>
      </Modal>
    </div>
  );
};

export default Agents;
