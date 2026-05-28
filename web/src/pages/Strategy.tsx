import React, { useState } from 'react';
import { Card, Form, Input, Button, InputNumber, Select, message, Result } from 'antd';
import { runBacktest } from '../api';

const Strategy: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<any>(null);
  const [form] = Form.useForm();

  const handleBacktest = async (values: any) => {
    setLoading(true);
    try {
      const res = await runBacktest({
        ...values,
        codes: values.codes.split(',').map((s: string) => s.trim()),
        startTime: new Date(values.startTime).toISOString(),
        endTime: new Date(values.endTime).toISOString(),
      });
      setResult(res.data);
      message.success('回测完成');
    } catch (error) {
      message.error('回测失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2>策略管理</h2>
      <Card title="回测配置">
        <Form form={form} layout="vertical" onFinish={handleBacktest}>
          <Form.Item label="策略" name="strategyName" rules={[{ required: true }]}>
            <Select>
              <Select.Option value="MABreakout">均线突破</Select.Option>
              <Select.Option value="GridTrading">网格交易</Select.Option>
              <Select.Option value="PairTrading">配对交易</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item label="股票代码" name="codes" rules={[{ required: true }]}>
            <Input placeholder="600519,000001" />
          </Form.Item>
          <Form.Item label="开始时间" name="startTime" rules={[{ required: true }]}>
            <Input type="date" />
          </Form.Item>
          <Form.Item label="结束时间" name="endTime" rules={[{ required: true }]}>
            <Input type="date" />
          </Form.Item>
          <Form.Item label="初始资金" name="initialCapital" initialValue={1000000}>
            <InputNumber min={10000} max={100000000} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item label="手续费率(%)" name="commissionRate" initialValue={0.03}>
            <InputNumber min={0} max={1} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={loading}>开始回测</Button>
          </Form.Item>
        </Form>
      </Card>

      {result && (
        <Card title="回测结果" style={{ marginTop: 16 }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16 }}>
            <div><strong>总收益率:</strong> {result.totalReturn?.toFixed(2)}%</div>
            <div><strong>年化收益:</strong> {result.annualizedReturn?.toFixed(2)}%</div>
            <div><strong>最大回撤:</strong> {result.maxDrawdown?.toFixed(2)}%</div>
            <div><strong>夏普比率:</strong> {result.sharpeRatio?.toFixed(2)}</div>
            <div><strong>胜率:</strong> {result.winRate?.toFixed(2)}%</div>
            <div><strong>盈亏比:</strong> {result.profitLossRatio?.toFixed(2)}</div>
            <div><strong>交易次数:</strong> {result.tradeCount}</div>
            <div><strong>初始资金:</strong> ¥{result.initialCapital?.toLocaleString()}</div>
            <div><strong>最终资金:</strong> ¥{result.finalCapital?.toLocaleString()}</div>
          </div>
        </Card>
      )}
    </div>
  );
};

export default Strategy;
