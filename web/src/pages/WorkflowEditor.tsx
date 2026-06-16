import React, { useState } from 'react';
import {
  Card, Button, Space, Select, message, Empty, Tag, Row, Col, Divider, Tooltip,
} from 'antd';
import { PlusOutlined, DeleteOutlined, PlayCircleOutlined } from '@ant-design/icons';
import { executeWorkflow } from '../api';
import type { WorkflowStep, WorkflowDefinition } from '../types/models';

interface DAGNode {
  id: string;
  stepId: string;
  agentId: string;
  taskType: string;
  dependsOn: string[];
  x: number;
  y: number;
}

/** [P2-10] Workflow 可视化编排 — DAG 拖拽式编排器 */
const WorkflowEditor: React.FC = () => {
  const [nodes, setNodes] = useState<DAGNode[]>([
    { id: '1', stepId: 'step1', agentId: 'research-agent', taskType: 'analyze-stock', dependsOn: [], x: 50, y: 50 },
    { id: '2', stepId: 'step2', agentId: 'alpha-agent', taskType: 'generate-signal', dependsOn: ['step1'], x: 300, y: 50 },
  ]);
  const [workflowName, setWorkflowName] = useState('自定义工作流');
  const [stockCode, setStockCode] = useState('000001');
  const [executing, setExecuting] = useState(false);
  const [execResult, setExecResult] = useState<Record<string, unknown> | null>(null);

  const addNode = () => {
    const id = String(nodes.length + 1);
    const y = 50 + nodes.length * 80;
    setNodes([
      ...nodes,
      { id, stepId: `step${id}`, agentId: 'research-agent', taskType: 'analyze-stock', dependsOn: [], x: 50, y },
    ]);
  };

  const removeNode = (id: string) => {
    setNodes(nodes.filter((n) => n.id !== id).map((n) => ({
      ...n,
      dependsOn: n.dependsOn.filter((d) => d !== nodes.find((x) => x.id === id)?.stepId),
    })));
  };

  const updateNode = (id: string, field: keyof DAGNode, value: string | string[]) => {
    setNodes(nodes.map((n) => (n.id === id ? { ...n, [field]: value } : n)));
  };

  const toggleDep = (nodeId: string, depStepId: string) => {
    setNodes(
      nodes.map((n) => {
        if (n.id !== nodeId) return n;
        const deps = n.dependsOn.includes(depStepId)
          ? n.dependsOn.filter((d) => d !== depStepId)
          : [...n.dependsOn, depStepId];
        return { ...n, dependsOn: deps };
      })
    );
  };

  const runWorkflow = async () => {
    const steps: WorkflowStep[] = nodes.map((n) => ({
      stepId: n.stepId,
      agentId: n.agentId,
      taskType: n.taskType,
      parameters: { code: stockCode },
      dependsOn: n.dependsOn,
    }));

    const workflow: WorkflowDefinition = {
      workflowId: `wf-${Date.now()}`,
      name: workflowName,
      steps,
    };

    setExecuting(true);
    try {
      const res = await executeWorkflow(workflow);
      setExecResult(res.data as unknown as Record<string, unknown>);
      message.success('工作流执行完成');
    } catch {
      message.error('工作流执行失败');
    } finally {
      setExecuting(false);
    }
  };

  return (
    <div>
      <h2>Workflow 编排</h2>

      {/* 工具栏 */}
      <Card style={{ marginBottom: 16 }}>
        <Space>
          <span style={{ fontWeight: 500 }}>名称:</span>
          <Select
            value={workflowName}
            onChange={setWorkflowName}
            style={{ width: 180 }}
            options={[
              { value: '自定义工作流', label: '自定义工作流' },
              { value: '分析+信号', label: '分析+信号' },
              { value: '全链路分析', label: '全链路分析' },
            ]}
          />
          <span style={{ fontWeight: 500, marginLeft: 16 }}>测试标的:</span>
          <Select
            value={stockCode}
            onChange={setStockCode}
            style={{ width: 150 }}
            options={[
              { value: '000001', label: '000001 平安银行' },
              { value: '000002', label: '000002 万科A' },
              { value: '600519', label: '600519 贵州茅台' },
            ]}
          />
          <Divider type="vertical" />
          <Button icon={<PlusOutlined />} onClick={addNode}>
            添加步骤
          </Button>
          <Button
            type="primary"
            icon={<PlayCircleOutlined />}
            onClick={runWorkflow}
            loading={executing}
          >
            执行
          </Button>
        </Space>
      </Card>

      {/* DAG 画布（简化版：卡片式布局 + 依赖标注） */}
      <Card title={`步骤列表 (${nodes.length})`} style={{ marginBottom: 16 }}>
        <Row gutter={[16, 16]}>
          {nodes.map((node, idx) => (
            <Col xs={24} sm={12} md={8} key={node.id}>
              <Card
                size="small"
                title={`Step ${idx + 1}`}
                extra={
                  <Button
                    danger
                    size="small"
                    icon={<DeleteOutlined />}
                    onClick={() => removeNode(node.id)}
                  />
                }
                style={{
                  borderLeft: `4px solid ${node.dependsOn.length > 0 ? '#faad14' : '#1677ff'}`,
                }}
              >
                <div style={{ marginBottom: 8 }}>
                  <span style={{ fontWeight: 500 }}>Agent:</span>
                  <Select
                    value={node.agentId}
                    onChange={(v) => updateNode(node.id, 'agentId', v)}
                    size="small"
                    style={{ width: '100%', marginTop: 4 }}
                    options={[
                      { value: 'research-agent', label: 'Research' },
                      { value: 'alpha-agent', label: 'Alpha' },
                      { value: 'risk-agent', label: 'Risk' },
                    ]}
                  />
                </div>
                <div style={{ marginBottom: 8 }}>
                  <span style={{ fontWeight: 500 }}>任务:</span>
                  <Select
                    value={node.taskType}
                    onChange={(v) => updateNode(node.id, 'taskType', v)}
                    size="small"
                    style={{ width: '100%', marginTop: 4 }}
                    options={[
                      { value: 'analyze-stock', label: '技术分析' },
                      { value: 'generate-signal', label: '信号生成' },
                      { value: 'check-risk', label: '风控检查' },
                    ]}
                  />
                </div>
                <div>
                  <span style={{ fontWeight: 500, fontSize: 12 }}>依赖:</span>
                  <div style={{ marginTop: 4 }}>
                    {nodes
                      .filter((n) => n.id !== node.id)
                      .map((n) => {
                        const isDep = node.dependsOn.includes(n.stepId);
                        return (
                          <Tooltip key={n.id} title={n.stepId}>
                            <Tag
                              color={isDep ? 'orange' : 'default'}
                              style={{ cursor: 'pointer', marginBottom: 4 }}
                              onClick={() => toggleDep(node.id, n.stepId)}
                            >
                              {isDep ? '← ' : ''}Step {nodes.indexOf(n) + 1}
                            </Tag>
                          </Tooltip>
                        );
                      })}
                    {nodes.filter((n) => n.id !== node.id).length === 0 && (
                      <Tag color="green">无依赖（可并行）</Tag>
                    )}
                  </div>
                </div>
              </Card>
            </Col>
          ))}
          {nodes.length === 0 && <Empty description="点击「添加步骤」开始编排" />}
        </Row>
      </Card>

      {/* 执行结果 */}
      {execResult && (
        <Card title="执行结果">
          <pre style={{ maxHeight: 400, overflow: 'auto', background: '#f5f5f5', padding: 12, borderRadius: 4 }}>
            {JSON.stringify(execResult, null, 2)}
          </pre>
        </Card>
      )}
    </div>
  );
};

export default WorkflowEditor;
