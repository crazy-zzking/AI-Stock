import React, { useEffect, useState } from 'react';
import { Card, Table, Tag, Row, Col, Spin, message, Button } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { getSectorRanking, getSectorStrongStocks } from '../api';
import type { SectorFlow } from '../api';
import Delta from '../components/Delta';

const Sector: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [sectors, setSectors] = useState<SectorFlow[]>([]);
  const [outSectors, setOutSectors] = useState<SectorFlow[]>([]);
  const [selected, setSelected] = useState<SectorFlow | null>(null);
  const [strongStocks, setStrongStocks] = useState<any[]>([]);

  const load = async () => {
    setLoading(true);
    try {
      const [inRes, outRes] = await Promise.all([
        getSectorRanking('inflow'),
        getSectorRanking('outflow'),
      ]);
      const data = inRes.data || [];
      setSectors(data);
      setOutSectors(outRes.data || []);
      if (data.length > 0) selectSector(data[0]);
    } catch {
      message.error('加载板块排行失败');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); /* eslint-disable-next-line */ }, []);

  const selectSector = async (s: SectorFlow) => {
    setSelected(s);
    try {
      const res = await getSectorStrongStocks(s.sectorCode);
      setStrongStocks(res.data || []);
    } catch {
      setStrongStocks([]);
    }
  };

  const inflow = sectors.slice(0, 15);     // 净流入榜（东财服务端按主力净流入降序）
  const outflow = outSectors.slice(0, 15); // 流出/弱势榜（独立查询，东财服务端排序）

  const sectorCols = [
    { title: '板块', dataIndex: 'sectorName', key: 'name' },
    { title: '涨幅', dataIndex: 'changePercent', key: 'chg', render: (v: number) => <Delta value={v} /> },
    { title: '主力净额', dataIndex: 'netInflow', key: 'net', render: (v: number) => <Delta value={v} mode="money" /> },
  ];

  const sectorTable = (title: string, data: SectorFlow[]) => (
    <Card title={title} size="small">
      <Table
        size="small"
        pagination={false}
        dataSource={data}
        rowKey="sectorCode"
        columns={sectorCols}
        onRow={(r) => ({
          onClick: () => selectSector(r),
          style: { cursor: 'pointer', background: selected?.sectorCode === r.sectorCode ? '#e6f4ff' : undefined },
        })}
        locale={{ emptyText: '暂无板块数据（需东财板块接口可达）' }}
      />
    </Card>
  );

  if (loading) return <Spin size="large" style={{ display: 'block', margin: '100px auto' }} />;

  return (
    <div>
      <h2>
        板块资金流
        <Button icon={<ReloadOutlined />} size="small" style={{ marginLeft: 12 }} onClick={load}>刷新</Button>
      </h2>

      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col xs={24} md={12}>{sectorTable('主力净流入榜 TOP15', inflow)}</Col>
        <Col xs={24} md={12}>{sectorTable('主力净流出榜 TOP15', outflow)}</Col>
      </Row>

      <Card title={`${selected?.sectorName || ''} — 板块内个股资金流（按主力净流入排序）`} size="small">
        <Table
          size="small"
          pagination={false}
          dataSource={strongStocks}
          rowKey="code"
          columns={[
            { title: '代码', dataIndex: 'code', key: 'code' },
            { title: '名称', dataIndex: 'name', key: 'name' },
            { title: '现价', dataIndex: 'price', key: 'price', render: (v: number) => (v ?? 0).toFixed(2) },
            { title: '涨幅', dataIndex: 'changePercent', key: 'chg', render: (v: number) => <Delta value={v} /> },
            { title: '主力净流入', dataIndex: 'mainNetInflow', key: 'net', render: (v: number) => <Delta value={v} mode="money" /> },
            { title: '主力占比', dataIndex: 'mainNetRatio', key: 'ratio', render: (v: number) => <Delta value={v} /> },
            { title: '', dataIndex: 'isLimitUp', key: 'lu', render: (v: boolean) => (v ? <Tag color="red">涨停</Tag> : null) },
          ]}
          locale={{ emptyText: '该板块暂无个股资金流数据（需东财接口可达）' }}
        />
      </Card>
    </div>
  );
};

export default Sector;
