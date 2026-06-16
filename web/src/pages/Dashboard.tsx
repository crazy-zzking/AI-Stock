import React, { useEffect, useState } from 'react';
import { Card, Row, Col, Statistic, message, Button } from 'antd';
import {
  StockOutlined,
  RiseOutlined,
  FallOutlined,
  RobotOutlined,
  ReloadOutlined,
  SyncOutlined,
  ClockCircleOutlined,
} from '@ant-design/icons';
import { getPositions, getAgents, getMarketState, refreshPositions } from '../api';
import { useNavigate } from 'react-router-dom';
import LoadingSkeleton from '../components/LoadingSkeleton';
import type { PositionSummary, AgentStatus } from '../types/models';

const Dashboard: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [positionData, setPositionData] = useState<PositionSummary | null>(null);
  const [agents, setAgents] = useState<AgentStatus[]>([]);
  const [marketState, setMarketState] = useState<number>(0);
  const [refreshing, setRefreshing] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      const [posRes, agentRes, marketRes] = await Promise.all([
        getPositions(),
        getAgents(),
        getMarketState(),
      ]);
      setPositionData(posRes.data);
      setAgents(agentRes.data);
      setMarketState(marketRes.data);
    } catch (error) {
      message.error('加载数据失败');
    } finally {
      setLoading(false);
    }
  };

  const handleRefreshPositions = async () => {
    setRefreshing(true);
    try {
      await refreshPositions();
      const posRes = await getPositions();
      setPositionData(posRes.data);
      message.success('持仓已刷新');
    } catch {
      message.error('刷新持仓失败');
    } finally {
      setRefreshing(false);
    }
  };

  const marketStateText = ['牛市', '熊市', '震荡', '极端', '未知'][marketState] || '未知';

  const formatUpdatedAt = (iso: string | null | undefined) => {
    if (!iso) return null;
    const d = new Date(iso);
    return d.toLocaleString('zh-CN', { hour12: false });
  };

  if (loading) {
    return <LoadingSkeleton rows={3} cardCount={4} />;
  }

  return (
    <div>
      <h2>
        Dashboard
        <Button
          icon={<ReloadOutlined />}
          size="small"
          style={{ marginLeft: 12 }}
          onClick={() => { setLoading(true); loadData(); }}
        >
          刷新
        </Button>
        <Button
          icon={<SyncOutlined spin={refreshing} />}
          size="small"
          style={{ marginLeft: 8 }}
          loading={refreshing}
          onClick={handleRefreshPositions}
        >
          主动刷新持仓
        </Button>
        {formatUpdatedAt(positionData?.updatedAt) && (
          <span style={{ marginLeft: 16, fontSize: 12, color: '#888' }}>
            <ClockCircleOutlined style={{ marginRight: 4 }} />
            缓存时间: {formatUpdatedAt(positionData?.updatedAt)}
          </span>
        )}
      </h2>
      <Row gutter={16}>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic
              title="总资产"
              value={positionData?.totalAssets || 0}
              precision={2}
              prefix="¥"
            />
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic
              title="持仓市值"
              value={positionData?.positionValue || 0}
              precision={2}
              prefix="¥"
            />
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic
              title="总盈亏"
              value={positionData?.totalProfit || 0}
              precision={2}
              prefix="¥"
              valueStyle={{ color: (positionData?.totalProfit || 0) >= 0 ? '#3f8600' : '#cf1322' }}
              suffix={(positionData?.totalProfit || 0) >= 0 ? <RiseOutlined /> : <FallOutlined />}
            />
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card>
            <Statistic
              title="持仓数量"
              value={positionData?.positionCount || 0}
              prefix={<StockOutlined />}
            />
          </Card>
        </Col>
      </Row>

      <Row gutter={16} style={{ marginTop: 16 }}>
        <Col xs={24} sm={12} md={8}>
          <Card title="市场状态" onClick={() => navigate('/auto-trading')} style={{ cursor: 'pointer' }}>
            <Statistic
              value={marketStateText}
              valueStyle={{ fontSize: 24 }}
            />
          </Card>
        </Col>
        <Col xs={24} sm={12} md={8}>
          <Card title="Agent状态">
            <Statistic
              value={agents.length}
              suffix="个在线"
              prefix={<RobotOutlined />}
            />
          </Card>
        </Col>
        <Col xs={24} sm={12} md={8}>
          <Card title="盈亏统计">
            <Row gutter={16}>
              <Col xs={12}>
                <Statistic
                  title="盈利"
                  value={positionData?.profitCount || 0}
                  valueStyle={{ color: '#3f8600' }}
                />
              </Col>
              <Col xs={12}>
                <Statistic
                  title="亏损"
                  value={positionData?.lossCount || 0}
                  valueStyle={{ color: '#cf1322' }}
                />
              </Col>
            </Row>
          </Card>
        </Col>
      </Row>
    </div>
  );
};

export default Dashboard;
