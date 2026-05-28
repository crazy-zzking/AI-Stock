import React, { useEffect, useState } from 'react';
import { Card, Tag, message, Select, Descriptions, Empty, Timeline } from 'antd';
import { HistoryOutlined } from '@ant-design/icons';
import { getAgents } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import type { AgentMemoryRecord } from '../types/models';

/** [P0] Agent 记忆 — 查看历史分析记录 */
const AgentMemory: React.FC = () => {
  const [agents, setAgents] = useState<{ agentId: string; name: string }[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<string | null>(null);
  const [stockCode, setStockCode] = useState('000001');
  const [records] = useState<AgentMemoryRecord[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadAgents();
  }, []);

  const loadAgents = async () => {
    try {
      const res = await getAgents();
      const list = res.data || [];
      setAgents(list.map((a) => ({ agentId: a.agentId, name: a.agentId })));
      if (list.length > 0) setSelectedAgent(list[0].agentId);
    } catch {
      message.error('加载Agent失败');
    } finally {
      setLoading(false);
    }
  };

  // Agent Memory 数据暂用模拟展示（待后端补充端点后替换）
  const renderMemoryPlaceholder = () => (
    <Card style={{ marginTop: 16 }}>
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={
          <span>
            Agent Memory 端点待后端补充（<code>GET /api/orchestrator/memory/{'{agentId}/{stockCode}'}</code>）
          </span>
        }
      />
    </Card>
  );

  if (loading) return <LoadingSkeleton />;

  return (
    <div>
      <h2><HistoryOutlined /> Agent 记忆</h2>

      {/* 查询条件 */}
      <Card style={{ marginBottom: 16 }}>
        <Descriptions column={3} size="small">
          <Descriptions.Item label="Agent">
            <Select
              value={selectedAgent}
              onChange={setSelectedAgent}
              style={{ width: 200 }}
              options={agents.map((a) => ({ value: a.agentId, label: a.name }))}
            />
          </Descriptions.Item>
          <Descriptions.Item label="股票代码">
            <Select
              value={stockCode}
              onChange={setStockCode}
              style={{ width: 160 }}
              showSearch
              options={[
                { value: '000001', label: '000001 平安银行' },
                { value: '000002', label: '000002 万科A' },
                { value: '600519', label: '600519 贵州茅台' },
              ]}
            />
          </Descriptions.Item>
        </Descriptions>

        {records.length > 0 ? (
          <Timeline
            style={{ marginTop: 16 }}
            items={records.map((r) => ({
              color: r.success ? 'green' : 'red',
              children: (
                <Card size="small">
                  <Descriptions column={2} size="small">
                    <Descriptions.Item label="时间">{new Date(r.createdAt).toLocaleString()}</Descriptions.Item>
                    <Descriptions.Item label="任务">{r.taskType}</Descriptions.Item>
                    <Descriptions.Item label="耗时">{r.executionTimeMs}ms</Descriptions.Item>
                    <Descriptions.Item label="状态">
                      <Tag color={r.success ? 'green' : 'red'}>{r.success ? '成功' : '失败'}</Tag>
                    </Descriptions.Item>
                    <Descriptions.Item label="信息" span={2}>{r.message}</Descriptions.Item>
                  </Descriptions>
                </Card>
              ),
            }))}
          />
        ) : (
          renderMemoryPlaceholder()
        )}
      </Card>
    </div>
  );
};

export default AgentMemory;
