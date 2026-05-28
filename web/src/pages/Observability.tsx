import React, { useEffect, useState } from 'react';
import { Card, Table, Tag, Row, Col, Statistic, message, Button } from 'antd';
import { DashboardOutlined, ReloadOutlined, LinkOutlined } from '@ant-design/icons';
import { getProviderStatus, getAgents, getHealth } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import type { ProviderStatus, AgentStatus } from '../types/models';

/** [P1] 观测面板 — 系统健康状态、Prometheus metrics */
const Observability: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [providers, setProviders] = useState<ProviderStatus[]>([]);
  const [agents, setAgents] = useState<AgentStatus[]>([]);
  const [health, setHealth] = useState<{ redis?: { connected: boolean } }>({});

  useEffect(() => {
    loadAll();
  }, []);

  const loadAll = async () => {
    setLoading(true);
    try {
      const [provRes, agentRes, healthRes] = await Promise.all([
        getProviderStatus(),
        getAgents(),
        getHealth(),
      ]);
      setProviders(provRes.data || []);
      setAgents(agentRes.data || []);
      setHealth(healthRes.data || {});
    } catch {
      message.error('加载监控数据失败');
    } finally {
      setLoading(false);
    }
  };

  const healthyProviders = providers.filter((p) => p.isHealthy).length;
  const onlineAgents = agents.filter((a) => a.isOnline).length;

  if (loading) return <LoadingSkeleton />;

  return (
    <div>
      <h2><DashboardOutlined /> 系统观测</h2>

      {/* 概览统计 */}
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={6}>
          <Card>
            <Statistic
              title="数据源"
              value={`${healthyProviders}/${providers.length}`}
              suffix="健康"
              valueStyle={{ color: healthyProviders === providers.length ? '#3f8600' : '#faad14' }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="Agent"
              value={`${onlineAgents}/${agents.length}`}
              suffix="在线"
              valueStyle={{ color: onlineAgents === agents.length ? '#3f8600' : '#faad14' }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="Redis"
              value={health.redis?.connected ? '正常' : '异常'}
              valueStyle={{ color: health.redis?.connected ? '#3f8600' : '#cf1322' }}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic title="操作" valueRender={() => (
              <Button icon={<ReloadOutlined />} onClick={loadAll} size="small">
                刷新
              </Button>
            )} />
          </Card>
        </Col>
      </Row>

      {/* 数据源详情 */}
      <Card title="数据源状态" style={{ marginBottom: 16 }}>
        <Table
          size="small"
          pagination={false}
          dataSource={providers}
          rowKey="providerId"
          columns={[
            { title: 'ID', dataIndex: 'providerId', key: 'providerId' },
            { title: '名称', dataIndex: 'providerName', key: 'providerName' },
            {
              title: '状态',
              dataIndex: 'isHealthy',
              key: 'isHealthy',
              render: (v: boolean) => (
                <Tag color={v ? 'green' : 'red'}>{v ? '健康' : '异常'}</Tag>
              ),
            },
            {
              title: '能力',
              dataIndex: 'capabilities',
              key: 'capabilities',
              render: (v: number[]) => (
                <span>{v.length} 项能力</span>
              ),
            },
          ]}
        />
      </Card>

      {/* Prometheus 链接 */}
      <Card title="监控工具">
        <Row gutter={16}>
          <Col span={8}>
            <Card size="small">
              <LinkOutlined style={{ marginRight: 8 }} />
              <a href="http://localhost:5000/metrics" target="_blank" rel="noreferrer">
                Prometheus Metrics
              </a>
            </Card>
          </Col>
        </Row>
      </Card>
    </div>
  );
};

export default Observability;
