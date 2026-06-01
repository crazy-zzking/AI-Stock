import React, { useEffect, useState } from 'react';
import { Card, Table, Tag, Row, Col, Spin, message, Button } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { getSectorRanking, getSectorStrongStocks } from '../api';
import type { SectorFlow } from '../api';

const yi = (v: number) => {
  const abs = Math.abs(v ?? 0);
  if (abs >= 1e8) return `${(v / 1e8).toFixed(2)}亿`;
  if (abs >= 1e4) return `${(v / 1e4).toFixed(0)}万`;
  return `${(v ?? 0).toFixed(0)}`;
};
const pct = (v: number) => `${v >= 0 ? '+' : ''}${(v ?? 0).toFixed(2)}%`;
const upDown = (v: number) => (v >= 0 ? '#cf1322' : '#3f8600'); // 红涨绿跌

const Sector: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [sectors, setSectors] = useState<SectorFlow[]>([]);
  const [selected, setSelected] = useState<SectorFlow | null>(null);
  const [strongStocks, setStrongStocks] = useState<any[]>([]);

  const load = async () => {
    setLoading(true);
    try {
      const res = await getSectorRanking();
      const data = res.data || [];
      setSectors(data);
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

  const inflow = sectors.slice(0, 15);                  // 净流入榜（已按净流入降序）
  const outflow = [...sectors].slice(-15).reverse();    // 净流出榜（尾部、流出最多在前）

  const sectorCols = [
    { title: '板块', dataIndex: 'sectorName', key: 'name' },
    { title: '涨幅', dataIndex: 'changePercent', key: 'chg', render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
    { title: '主力净额', dataIndex: 'netInflow', key: 'net', render: (v: number) => <span style={{ color: upDown(v) }}>{yi(v)}</span> },
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
        <Col span={12}>{sectorTable('主力净流入榜 TOP15', inflow)}</Col>
        <Col span={12}>{sectorTable('主力净流出榜 TOP15', outflow)}</Col>
      </Row>

      <Card title={`${selected?.sectorName || ''} — 板块内强势个股（资金流入 + 上涨）`} size="small">
        <Table
          size="small"
          pagination={false}
          dataSource={strongStocks}
          rowKey="code"
          columns={[
            { title: '代码', dataIndex: 'code', key: 'code' },
            { title: '名称', dataIndex: 'name', key: 'name' },
            { title: '涨幅', dataIndex: 'changePercent', key: 'chg', render: (v: number) => <span style={{ color: upDown(v) }}>{pct(v)}</span> },
            { title: '主力净流入', dataIndex: 'mainNetInflow', key: 'net', render: (v: number) => <span style={{ color: upDown(v) }}>{yi(v)}</span> },
            { title: '换手', dataIndex: 'turnoverRate', key: 'turn', render: (v: number) => `${(v ?? 0).toFixed(2)}%` },
            { title: '', dataIndex: 'isLimitUp', key: 'lu', render: (v: boolean) => (v ? <Tag color="red">涨停</Tag> : null) },
          ]}
          locale={{ emptyText: '该板块暂无强势个股（需 daily_market_snapshot 有当日数据）' }}
        />
      </Card>
    </div>
  );
};

export default Sector;
