import React, { useEffect, useState } from 'react';
import { Card, Row, Col, Statistic, Spin, message } from 'antd';
import {
  StockOutlined,
  RiseOutlined,
  FallOutlined,
  RobotOutlined,
} from '@ant-design/icons';
import { getPositions, getAgents, getMarketState } from '../api';

const Dashboard: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [positionData, setPositionData] = useState<any>(null);
  const [agents, setAgents] = useState<any[]>([]);
  const [marketState, setMarketState] = useState<number>(0);

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

  const marketStateText = ['牛市', '熊市', '震荡', '极端', '未知'][marketState] || '未知';

  if (loading) {
    return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  }

  return (
    <div>
      <h2>Dashboard</h2>
      <Row gutter={16}>
        <Col span={6}>
          <Card>
            <Statistic
              title="总资产"
              value={positionData?.totalAssets || 0}
              precision={2}
              prefix="¥"
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic
              title="持仓市值"
              value={positionData?.positionValue || 0}
              precision={2}
              prefix="¥"
            />
          </Card>
        </Col>
        <Col span={6}>
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
        <Col span={6}>
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
        <Col span={8}>
          <Card title="市场状态">
            <Statistic
              value={marketStateText}
              valueStyle={{ fontSize: 24 }}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card title="Agent状态">
            <Statistic
              value={agents.length}
              suffix="个在线"
              prefix={<RobotOutlined />}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card title="盈亏统计">
            <Row gutter={16}>
              <Col span={12}>
                <Statistic
                  title="盈利"
                  value={positionData?.profitCount || 0}
                  valueStyle={{ color: '#3f8600' }}
                />
              </Col>
              <Col span={12}>
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
