import React, { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Card, Table, Tabs, Tag, message, Select, Empty, Row, Col, Input, Button, Space, List } from 'antd';
import { ApartmentOutlined } from '@ant-design/icons';
import { getChains, getChainCompanies, getCompanyRelations, getSuppliers, getCustomers, diffuseConcept, findRelationPath, getCandidateEdges, getCandidateValues } from '../api';
import type { CandidateEdge } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import RelationGraph from '../components/RelationGraph';
import CandidateGraph from '../components/CandidateGraph';
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

  // 概念扩散 / 关系路径
  const [diffEvent, setDiffEvent] = useState('');
  const [diffConcepts, setDiffConcepts] = useState('');
  const [diffResult, setDiffResult] = useState<any>(null);
  const [pathFrom, setPathFrom] = useState('');
  const [pathTo, setPathTo] = useState('');
  const [pathResult, setPathResult] = useState<any>(null);

  // 候选关系图谱（graph_candidate_edge）
  const [candEdgeType, setCandEdgeType] = useState<string>('concept');
  const [candValue, setCandValue] = useState<string | undefined>(undefined);
  const [candValues, setCandValues] = useState<{ value: string; count: number }[]>([]);
  const [candidateEdges, setCandidateEdges] = useState<CandidateEdge[]>([]);

  useEffect(() => {
    loadChains();
  }, []);

  // edgeType 变 → 加载可选具体值 + 重置已选值
  useEffect(() => {
    setCandValue(undefined);
    if (candEdgeType === 'all') { setCandValues([]); return; }
    getCandidateValues(candEdgeType)
      .then((r) => setCandValues(r.data || []))
      .catch(() => setCandValues([]));
  }, [candEdgeType]);

  // edgeType / 具体值 变 → 加载候选边
  useEffect(() => {
    getCandidateEdges(candEdgeType === 'all' ? undefined : candEdgeType, candValue)
      .then((r) => setCandidateEdges(r.data || []))
      .catch(() => setCandidateEdges([]));
  }, [candEdgeType, candValue]);

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

  const handleDiffuse = async () => {
    if (!diffEvent.trim() || !diffConcepts.trim()) { message.warning('请填核心事件和关联概念'); return; }
    try {
      const concepts = diffConcepts.split(/[,，]/).map((s) => s.trim()).filter(Boolean);
      const res = await diffuseConcept(diffEvent.trim(), concepts);
      setDiffResult(res.data);
    } catch { message.error('概念扩散失败'); }
  };

  const handleFindPath = async () => {
    if (!pathFrom.trim() || !pathTo.trim()) { message.warning('请填起点和终点代码'); return; }
    try {
      const res = await findRelationPath(pathFrom.trim(), pathTo.trim());
      setPathResult(res.data);
    } catch { message.error('路径查找失败'); }
  };

  if (loading) return <LoadingSkeleton />;

  return (
    <div>
      <h2>知识图谱</h2>

      {/* 候选关系图谱 — graph_candidate_edge（情报推断，含未晋升线索） */}
      <Card title="候选关系图谱（情报推断 · graph_candidate_edge）" style={{ marginBottom: 16 }}>
        <Space style={{ marginBottom: 12 }}>
          <span style={{ fontWeight: 500 }}>边类型:</span>
          <Select
            value={candEdgeType}
            onChange={setCandEdgeType}
            style={{ width: 220 }}
            options={[
              { value: 'concept', label: '公司-概念 (concept)' },
              { value: 'co-occur', label: '公司-公司共现 (co-occur)' },
              { value: 'all', label: '全部' },
            ]}
          />
          {candEdgeType !== 'all' && (
            <Select
              showSearch
              allowClear
              value={candValue}
              onChange={setCandValue}
              style={{ width: 260 }}
              placeholder={candEdgeType === 'concept' ? '选具体概念（可选）' : '选具体公司（可选）'}
              options={candValues.map((v) => ({
                value: v.value,
                label: v.count > 0 ? `${v.value}（${v.count}）` : v.value,
              }))}
              filterOption={(input, opt) => String(opt?.label ?? '').includes(input)}
              notFoundContent={candValues.length === 0 ? '暂无可选值' : undefined}
            />
          )}
          <span style={{ color: '#999', fontSize: 12 }}>
            绿实线=已晋升权威图谱，灰虚线=候选中；线越粗提及越多
          </span>
        </Space>
        <CandidateGraph edges={candidateEdges} />
      </Card>

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

      {/* 概念扩散推演 + 关系路径查找 */}
      <Row gutter={16} style={{ marginTop: 16 }}>
        <Col span={12}>
          <Card title="概念扩散推演" size="small">
            <Space direction="vertical" style={{ width: '100%' }}>
              <Input placeholder="核心事件（如：某政策利好半导体）" value={diffEvent} onChange={(e) => setDiffEvent(e.target.value)} />
              <Input placeholder="关联概念，逗号分隔（如：半导体,国产替代）" value={diffConcepts} onChange={(e) => setDiffConcepts(e.target.value)} />
              <Button type="primary" onClick={handleDiffuse}>推演扩散</Button>
              {diffResult && (
                <div>
                  <div style={{ marginTop: 4 }}>
                    <b>关联产业链：</b>
                    {(diffResult.relatedChains || []).map((c: string) => <Tag color="purple" key={c}>{c}</Tag>)}
                  </div>
                  <div style={{ marginTop: 8 }}>
                    <b>受益公司：</b>
                    {(diffResult.beneficiaryCompanies || []).map((c: any, idx: number) =>
                      <Tag color="green" key={c.code || c.name || idx}>{c.name || c.code || String(c)}</Tag>)}
                  </div>
                </div>
              )}
            </Space>
          </Card>
        </Col>
        <Col span={12}>
          <Card title="关系路径查找" size="small">
            <Space direction="vertical" style={{ width: '100%' }}>
              <Input placeholder="起点公司代码" value={pathFrom} onChange={(e) => setPathFrom(e.target.value)} />
              <Input placeholder="终点公司代码" value={pathTo} onChange={(e) => setPathTo(e.target.value)} />
              <Button type="primary" onClick={handleFindPath}>查找路径</Button>
              {pathResult && (
                Array.isArray(pathResult) && pathResult.length > 0 ? (
                  <List
                    size="small"
                    dataSource={pathResult}
                    renderItem={(p: any, idx: number) => (
                      <List.Item>路径{idx + 1}：{Array.isArray(p) ? p.join(' → ') : JSON.stringify(p)}</List.Item>
                    )}
                  />
                ) : <Empty description="无关联路径" />
              )}
            </Space>
          </Card>
        </Col>
      </Row>
    </div>
  );
};

export default KnowledgeGraph;
