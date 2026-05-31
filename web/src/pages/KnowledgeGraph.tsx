import React, { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Card, Table, Tabs, Tag, message, Select, Empty, Row, Col } from 'antd';
import { ApartmentOutlined } from '@ant-design/icons';
import { getChains, getChainCompanies, getCompanyRelations, getSuppliers, getCustomers } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import RelationGraph from '../components/RelationGraph';
import type { CompanyRelation } from '../types/models';

/** [P1+P2-8] 知识图谱 — 产业链 + 公司关系可视化 */
const KnowledgeGraph: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [chains, setChains] = useState<string[]>([]);
  const [selectedChain, setSelectedChain] = useState<string | null>(null);
  const [chainCompanies, setChainCompanies] = useState<any[]>([]);
  const [selectedCompany, setSelectedCompany] = useState<string | null>(null);
  const [relations, setRelations] = useState<CompanyRelation[]>([]);
  const [suppliers, setSuppliers] = useState<any[]>([]);
  const [customers, setCustomers] = useState<any[]>([]);
  const [searchParams] = useSearchParams();

  useEffect(() => {
    loadChains();
  }, []);

  const loadChains = async () => {
    try {
      const res = await getChains();
      setChains(res.data || []);
      if (res.data?.length > 0) setSelectedChain(res.data[0]);
    } catch {
      message.error('加载产业链失败');
    } finally {
      setLoading(false);
    }
  };

  const loadChainCompanies = async (chainName: string) => {
    try {
      const res = await getChainCompanies(chainName);
      setChainCompanies(res.data || []);
    } catch {
      message.error('加载产业链公司失败');
    }
  };

  useEffect(() => {
    if (selectedChain) loadChainCompanies(selectedChain);
  }, [selectedChain]);

  const loadCompanyDetail = async (code: string) => {
    setSelectedCompany(code);
    try {
      const [relRes, supRes, cusRes] = await Promise.all([
        getCompanyRelations(code),
        getSuppliers(code),
        getCustomers(code),
      ]);
      setRelations(relRes.data || []);
      setSuppliers(supRes.data || []);
      setCustomers(cusRes.data || []);
    } catch {
      message.error('加载公司关系失败');
    }
  };

  // 选股页跳转带 ?code=xxx 时，直接加载该公司图谱
  useEffect(() => {
    const code = searchParams.get('code');
    if (code) loadCompanyDetail(code);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  if (loading) return <LoadingSkeleton />;

  return (
    <div>
      <h2>知识图谱</h2>

      {/* 产业链选择 */}
      <Card style={{ marginBottom: 16 }}>
        <Row gutter={16} align="middle">
          <Col>
            <ApartmentOutlined style={{ fontSize: 20, marginRight: 8 }} />
            <span style={{ fontWeight: 500 }}>产业链:</span>
          </Col>
          <Col>
            <Select
              value={selectedChain}
              onChange={setSelectedChain}
              style={{ width: 200 }}
              options={chains.map((c) => ({ value: c, label: c }))}
            />
          </Col>
        </Row>
      </Card>

      <Row gutter={16}>
        {/* 产业链公司列表 */}
        <Col span={10}>
          <Card title={`${selectedChain || ''} — 公司列表`} style={{ marginBottom: 16 }}>
            <Table
              size="small"
              pagination={false}
              dataSource={chainCompanies}
              rowKey="code"
              columns={[
                { title: '代码', dataIndex: 'code', key: 'code' },
                { title: '名称', dataIndex: 'name', key: 'name' },
                { title: '角色', dataIndex: 'role', key: 'role', render: (v: string) => <Tag>{v || '-'}</Tag> },
              ]}
              onRow={(record) => ({
                onClick: () => loadCompanyDetail(record.code),
                style: {
                  cursor: 'pointer',
                  background: selectedCompany === record.code ? '#e6f4ff' : undefined,
                },
              })}
            />
          </Card>
        </Col>

        {/* 公司关系详情 */}
        <Col span={14}>
          {selectedCompany ? (
            <Tabs
              items={[
                {
                  key: 'graph',
                  label: '关系图谱',
                  children: (
                    <RelationGraph
                      center={selectedCompany}
                      relations={relations as any}
                      suppliers={suppliers}
                      customers={customers}
                    />
                  ),
                },
                {
                  key: 'relations',
                  label: '关联关系',
                  children: relations.length > 0 ? (
                    <Table
                      size="small"
                      pagination={false}
                      dataSource={relations}
                      rowKey="relatedCode"
                      columns={[
                        { title: '关联方', dataIndex: 'relatedCode', key: 'relatedCode' },
                        { title: '关系类型', dataIndex: 'relationType', key: 'relationType', render: (v: string) => <Tag color="blue">{v}</Tag> },
                        { title: '权重', dataIndex: 'weight', key: 'weight' },
                      ]}
                    />
                  ) : (
                    <Empty description="暂无关联关系数据" />
                  ),
                },
                {
                  key: 'suppliers',
                  label: `上游供应商 (${suppliers.length})`,
                  children: suppliers.length > 0 ? (
                    <Table
                      size="small"
                      pagination={false}
                      dataSource={suppliers}
                      rowKey="code"
                      columns={[
                        { title: '代码', dataIndex: 'code', key: 'code' },
                        { title: '名称', dataIndex: 'name', key: 'name' },
                      ]}
                    />
                  ) : (
                    <Empty description="暂无供应商数据" />
                  ),
                },
                {
                  key: 'customers',
                  label: `下游客户 (${customers.length})`,
                  children: customers.length > 0 ? (
                    <Table
                      size="small"
                      pagination={false}
                      dataSource={customers}
                      rowKey="code"
                      columns={[
                        { title: '代码', dataIndex: 'code', key: 'code' },
                        { title: '名称', dataIndex: 'name', key: 'name' },
                      ]}
                    />
                  ) : (
                    <Empty description="暂无客户数据" />
                  ),
                },
              ]}
            />
          ) : (
            <Card>
              <Empty description="点击左侧公司查看关联关系" />
            </Card>
          )}
        </Col>
      </Row>
    </div>
  );
};

export default KnowledgeGraph;
