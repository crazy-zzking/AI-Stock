import React, { useEffect, useState } from 'react';
import { Card, Table, Statistic, Row, Col, Spin, message, Tag } from 'antd';
import { getPositions } from '../api';

const Positions: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [data, setData] = useState<any>(null);

  useEffect(() => {
    loadPositions();
  }, []);

  const loadPositions = async () => {
    try {
      const res = await getPositions();
      setData(res.data);
    } catch (error) {
      message.error('加载持仓失败');
    } finally {
      setLoading(false);
    }
  };

  const columns = [
    { title: '代码', dataIndex: 'code', key: 'code' },
    { title: '名称', dataIndex: 'name', key: 'name' },
    { title: '数量', dataIndex: 'volume', key: 'volume' },
    { 
      title: '成本价', 
      dataIndex: 'costPrice', 
      key: 'costPrice',
      render: (v: number) => `¥${v?.toFixed(2)}`,
    },
    { 
      title: '现价', 
      dataIndex: 'currentPrice', 
      key: 'currentPrice',
      render: (v: number) => `¥${v?.toFixed(2)}`,
    },
    { 
      title: '市值', 
      dataIndex: 'marketValue', 
      key: 'marketValue',
      render: (v: number) => `¥${v?.toFixed(2)}`,
    },
    { 
      title: '盈亏', 
      dataIndex: 'profit', 
      key: 'profit',
      render: (v: number) => (
        <span style={{ color: v >= 0 ? '#3f8600' : '#cf1322' }}>
          ¥{v?.toFixed(2)}
        </span>
      ),
    },
    { 
      title: '盈亏比例', 
      dataIndex: 'profitRate', 
      key: 'profitRate',
      render: (v: number) => (
        <Tag color={v >= 0 ? 'green' : 'red'}>
          {v?.toFixed(2)}%
        </Tag>
      ),
    },
  ];

  if (loading) {
    return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;
  }

  return (
    <div>
      <h2>持仓管理</h2>
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={6}>
          <Card>
            <Statistic title="总资产" value={data?.totalAssets || 0} prefix="¥" precision={2} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic title="可用资金" value={data?.availableBalance || 0} prefix="¥" precision={2} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic title="持仓市值" value={data?.positionValue || 0} prefix="¥" precision={2} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic 
              title="总盈亏" 
              value={data?.totalProfit || 0} 
              prefix="¥" 
              precision={2}
              valueStyle={{ color: (data?.totalProfit || 0) >= 0 ? '#3f8600' : '#cf1322' }}
            />
          </Card>
        </Col>
      </Row>
      <Card>
        <Table 
          columns={columns} 
          dataSource={data?.positions || []} 
          rowKey="code"
          pagination={false}
        />
      </Card>
    </div>
  );
};

export default Positions;
