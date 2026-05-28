import React, { useState } from 'react';
import { Card, Button, Space, Row, Col, Statistic, message, Steps, InputNumber } from 'antd';
import { ThunderboltOutlined } from '@ant-design/icons';
import StockSearch from '../components/StockSearch';
import ResultPanel from '../components/ResultPanel';
import LoadingSkeleton from '../components/LoadingSkeleton';
import { makeDecision, makeBatchDecision } from '../api';
import type { StockInfo, DecisionResult } from '../types/models';

/** [P1+P2-8] 自主交易面板 — 一键决策 + 多 Agent 结果并列 */
const AutoTrading: React.FC = () => {
  const [selectedStock, setSelectedStock] = useState<StockInfo | null>(null);
  const [capital, setCapital] = useState(100000);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<DecisionResult | null>(null);
  const [batchResults, setBatchResults] = useState<DecisionResult[]>([]);
  const [currentStep, setCurrentStep] = useState(0);

  const stepLabels = ['选择标的', '执行分析', '信号生成', '风控检查', '订单下发'];

  const handleSingleDecision = async () => {
    if (!selectedStock) {
      message.warning('请先选择股票');
      return;
    }
    setLoading(true);
    setCurrentStep(1);
    try {
      const res = await makeDecision(selectedStock.code, capital);
      setResult(res.data);
      setBatchResults([]);
      setCurrentStep(4);
      message.success(`决策完成: ${res.data.message}`);
    } catch {
      message.error('决策执行失败');
      setCurrentStep(0);
    } finally {
      setLoading(false);
    }
  };

  const handleBatchDecision = async () => {
    if (!selectedStock) {
      message.warning('请先选择股票');
      return;
    }
    setLoading(true);
    try {
      const res = await makeBatchDecision([selectedStock.code], capital);
      setBatchResults(res.data || []);
      setResult(null);
      message.success(`批量决策完成: ${res.data?.length || 0} 只`);
    } catch {
      message.error('批量决策失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2>自主交易</h2>

      {/* 操作区 */}
      <Card style={{ marginBottom: 16 }}>
        <Row gutter={16} align="middle">
          <Col>
            <span style={{ marginRight: 8, fontWeight: 500 }}>标的:</span>
            <StockSearch onSelect={setSelectedStock} />
          </Col>
          <Col>
            <span style={{ marginRight: 8, fontWeight: 500 }}>资金:</span>
            <InputNumber
              value={capital}
              onChange={(v) => setCapital(v || 100000)}
              min={1000}
              max={100000000}
              step={10000}
              formatter={(v) => `¥ ${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
              style={{ width: 160 }}
            />
          </Col>
          <Col>
            <Space>
              <Button
                type="primary"
                icon={<ThunderboltOutlined />}
                onClick={handleSingleDecision}
                loading={loading}
              >
                一键决策
              </Button>
              <Button onClick={handleBatchDecision} loading={loading}>
                批量决策
              </Button>
            </Space>
          </Col>
        </Row>

        {selectedStock && (
          <div style={{ marginTop: 12 }}>
            <span style={{ color: '#666' }}>
              已选: <strong>{selectedStock.code}</strong> {selectedStock.name}
            </span>
          </div>
        )}
      </Card>

      {/* 进度步骤 */}
      <Card style={{ marginBottom: 16 }}>
        <Steps
          current={currentStep}
          size="small"
          items={stepLabels.map((title) => ({ title }))}
        />
      </Card>

      {/* 加载状态 */}
      {loading && <LoadingSkeleton rows={4} showCards={false} />}

      {/* 单股决策结果 */}
      {result && !loading && (
        <Card title={`${selectedStock?.code} ${selectedStock?.name || ''} 决策详情`}>
          {/* 概览统计 */}
          <Row gutter={16} style={{ marginBottom: 16 }}>
            <Col span={6}>
              <Statistic title="信号数" value={result.signals?.signals?.length || 0} />
            </Col>
            <Col span={6}>
              <Statistic
                title="订单数"
                value={result.orders?.length || 0}
                valueStyle={{ color: result.orders?.length ? '#1677ff' : '#999' }}
              />
            </Col>
            <Col span={6}>
              <Statistic title="风控" value={result.riskCheck ? '已通过' : '失败'} />
            </Col>
            <Col span={6}>
              <Statistic title="耗时" value={result.executionTime} suffix="ms" />
            </Col>
          </Row>
          <ResultPanel result={result} mode="decision" />
        </Card>
      )}

      {/* 批量决策结果 */}
      {batchResults.length > 0 && !loading && (
        <Card title="批量决策结果">
          {batchResults.map((r) => (
            <Card
              key={r.code}
              size="small"
              title={`${r.code} — ${r.success ? '成功' : '失败'}`}
              style={{ marginBottom: 8 }}
            >
              <ResultPanel result={r} mode="decision" />
            </Card>
          ))}
        </Card>
      )}
    </div>
  );
};

export default AutoTrading;
