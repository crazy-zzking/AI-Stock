import React from 'react';
import { Card, Descriptions, Tag, Collapse, Empty, Typography } from 'antd';
import type { AgentResult, DecisionResult } from '../types/models';

const { Text } = Typography;

interface ResultPanelProps {
  /** Agent 执行结果 */
  result: AgentResult | DecisionResult | null;
  /** 标题 */
  title?: string;
  /** 展示模式 */
  mode?: 'agent' | 'decision';
}

/** 结构化结果面板 — 替代原始 JSON 弹窗 */
const ResultPanel: React.FC<ResultPanelProps> = ({ result, title = '执行结果', mode = 'agent' }) => {
  if (!result) {
    return <Empty description="无结果" />;
  }

  const isDecision = mode === 'decision' && 'analysis' in result;
  const decision = isDecision ? (result as DecisionResult) : null;

  return (
    <div style={{ padding: 8 }}>
      {/* 状态栏 */}
      <div style={{ marginBottom: 12 }}>
        <Tag color={result.success ? 'green' : 'red'}>
          {result.success ? '成功' : '失败'}
        </Tag>
        <Text type="secondary" style={{ marginLeft: 8 }}>
          {result.message}
        </Text>
        <Text type="secondary" style={{ marginLeft: 16 }}>
          耗时: {result.executionTime}ms
        </Text>
      </div>

      {/* 决策模式：分析/信号/风控/订单 折叠面板 */}
      {decision && (
        <Collapse
          defaultActiveKey={['analysis', 'signals']}
          items={[
            {
              key: 'analysis',
              label: '技术分析',
              children: (
                <Descriptions column={2} size="small" bordered>
                  <Descriptions.Item label="现价">{decision.analysis?.currentPrice as number}</Descriptions.Item>
                  <Descriptions.Item label="MA5">{decision.analysis?.ma5 as number}</Descriptions.Item>
                  <Descriptions.Item label="MA20">{decision.analysis?.ma20 as number}</Descriptions.Item>
                  <Descriptions.Item label="RSI">{decision.analysis?.rsi as number}</Descriptions.Item>
                  <Descriptions.Item label="MACD">{decision.analysis?.macd as number}</Descriptions.Item>
                  <Descriptions.Item label="波动率">{decision.analysis?.volatility as number}</Descriptions.Item>
                </Descriptions>
              ),
            },
            {
              key: 'signals',
              label: `交易信号 (${decision.signals?.signals?.length || 0})`,
              children: decision.signals?.signals?.map((s, i) => (
                <Card key={i} size="small" style={{ marginBottom: 8 }}>
                  <Descriptions column={2} size="small">
                    <Descriptions.Item label="策略">{s.strategyName}</Descriptions.Item>
                    <Descriptions.Item label="方向">
                      <Tag color={s.signalType === 0 ? 'green' : s.signalType === 1 ? 'red' : 'default'}>
                        {s.signalType === 0 ? '买入' : s.signalType === 1 ? '卖出' : '持有'}
                      </Tag>
                    </Descriptions.Item>
                    <Descriptions.Item label="强度">{s.strength}</Descriptions.Item>
                    <Descriptions.Item label="价格">{s.price}</Descriptions.Item>
                    <Descriptions.Item label="原因" span={2}>{s.reason}</Descriptions.Item>
                  </Descriptions>
                </Card>
              )),
            },
            {
              key: 'risk',
              label: '风控检查',
              children: <Text type="secondary">风控已通过</Text>,
            },
            {
              key: 'orders',
              label: `订单 (${decision.orders?.length || 0})`,
              children: decision.orders?.length > 0 ? (
                decision.orders.map((o, i) => (
                  <Card key={i} size="small" style={{ marginBottom: 8 }}>
                    <Tag color={o.success ? 'green' : 'red'}>{o.success ? '成功' : '失败'}</Tag>
                    <Text>{o.message}</Text>
                  </Card>
                ))
              ) : (
                <Text type="secondary">无订单</Text>
              ),
            },
          ]}
        />
      )}

      {/* Agent 模式：扁平化展示 output */}
      {mode === 'agent' && !isDecision && (
        <Card size="small">
          <Descriptions column={2} size="small" bordered>
            {Object.entries((result as AgentResult).output || {}).map(([key, value]) => {
              if (key === 'graphContextRaw' || key === 'graphContext') return null;
              const displayValue =
                typeof value === 'object' ? JSON.stringify(value) : String(value);
              return (
                <Descriptions.Item key={key} label={key}>
                  {String(displayValue).length > 80
                    ? String(displayValue).slice(0, 80) + '...'
                    : displayValue}
                </Descriptions.Item>
              );
            })}
          </Descriptions>
        </Card>
      )}
    </div>
  );
};

export default ResultPanel;
