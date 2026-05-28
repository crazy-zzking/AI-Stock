import React, { useEffect, useState, useRef, useCallback } from 'react';
import { Card, Descriptions, Row, Col, Statistic, message, Tabs } from 'antd';
import { createChart, ColorType, IChartApi, CandlestickSeries, HistogramSeries, Time } from 'lightweight-charts';
import { getQuote, getKlines } from '../api';
import LoadingSkeleton from '../components/LoadingSkeleton';
import StockSearch from '../components/StockSearch';
import type { StockInfo, QuoteData, KlineData } from '../types/models';

/** [P2-8] 股票详情页 — K线图 + 行情数据 */
const StockDetail: React.FC = () => {
  const [stock, setStock] = useState<StockInfo | null>(null);
  const [quote, setQuote] = useState<QuoteData | null>(null);
  const [klines, setKlines] = useState<KlineData[]>([]);
  const [loading, setLoading] = useState(false);
  const chartContainerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);

  const renderChart = useCallback(() => {
    if (!chartContainerRef.current || klines.length === 0) return;
    chartRef.current?.remove();

    const chart = createChart(chartContainerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: '#ffffff' },
        textColor: '#333',
      },
      grid: {
        vertLines: { color: '#f0f0f0' },
        horzLines: { color: '#f0f0f0' },
      },
      width: chartContainerRef.current.clientWidth,
      height: 480,
      crosshair: { mode: 0 },
      timeScale: {
        borderColor: '#e5e5e5',
        timeVisible: true,
      },
    });

    const candleSeries = chart.addSeries(CandlestickSeries, {
      upColor: '#cf1322',
      downColor: '#3f8600',
      borderDownColor: '#3f8600',
      borderUpColor: '#cf1322',
      wickDownColor: '#3f8600',
      wickUpColor: '#cf1322',
    });

    const candleData = klines.map((k) => ({
      time: (new Date(k.dateTime).getTime() / 1000) as Time,
      open: Number(k.open),
      high: Number(k.high),
      low: Number(k.low),
      close: Number(k.close),
    }));
    candleSeries.setData(candleData);

    const volumeSeries = chart.addSeries(HistogramSeries, {
      color: '#26a69a',
      priceFormat: { type: 'volume' },
      priceScaleId: 'volume',
    });
    chart.priceScale('volume').applyOptions({
      scaleMargins: { top: 0.8, bottom: 0 },
    });

    const volumeData = klines.map((k) => ({
      time: (new Date(k.dateTime).getTime() / 1000) as Time,
      value: Number(k.volume),
      color: Number(k.close) >= Number(k.open) ? 'rgba(239,83,80,0.3)' : 'rgba(38,166,154,0.3)',
    }));
    volumeSeries.setData(volumeData);

    chart.timeScale().fitContent();
    chartRef.current = chart;
  }, [klines]);

  useEffect(() => {
    if (stock) loadData(stock.code);
  }, [stock]);

  useEffect(() => {
    if (klines.length > 0) renderChart();
    return () => { chartRef.current?.remove(); chartRef.current = null; };
  }, [klines, renderChart]);

  const loadData = async (code: string) => {
    setLoading(true);
    try {
      const [qRes, kRes] = await Promise.all([
        getQuote(code),
        getKlines(code, 'Daily', 200),
      ]);
      setQuote(qRes.data);
      setKlines(kRes.data || []);
    } catch {
      message.error('加载数据失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2>股票详情</h2>

      <Card style={{ marginBottom: 16 }}>
        <Row gutter={16} align="middle">
          <Col>
            <span style={{ fontWeight: 500, marginRight: 8 }}>搜索:</span>
            <StockSearch onSelect={setStock} style={{ width: 280 }} />
          </Col>
        </Row>
      </Card>

      {loading && <LoadingSkeleton rows={6} showCards />}

      {!stock && !loading && (
        <Card>
          <div style={{ textAlign: 'center', padding: 40, color: '#999' }}>
            请输入股票代码搜索，查看实时行情和K线图
          </div>
        </Card>
      )}

      {quote && !loading && (
        <>
          {/* 行情概览 */}
          <Card style={{ marginBottom: 16 }}>
            <Row gutter={16}>
              <Col span={6}>
                <Statistic
                  title={`${stock?.name} (${stock?.code})`}
                  value={quote.price}
                  precision={2}
                  valueStyle={{
                    color: quote.changePercent >= 0 ? '#cf1322' : '#3f8600',
                    fontSize: 32,
                  }}
                  prefix="¥"
                />
              </Col>
              <Col span={6}>
                <Statistic
                  title="涨跌幅"
                  value={quote.changePercent}
                  precision={2}
                  suffix="%"
                  valueStyle={{ color: quote.changePercent >= 0 ? '#cf1322' : '#3f8600' }}
                />
              </Col>
              <Col span={6}>
                <Statistic title="成交量" value={quote.volume} />
              </Col>
              <Col span={6}>
                <Statistic title="成交额" value={quote.amount} precision={2} prefix="¥" />
              </Col>
            </Row>
          </Card>

          {/* K线图 + 数据 */}
          <Tabs
            items={[
              {
                key: 'chart',
                label: 'K线图',
                children: (
                  <Card>
                    <div ref={chartContainerRef} style={{ width: '100%', minHeight: 480 }} />
                  </Card>
                ),
              },
              {
                key: 'data',
                label: '行情数据',
                children: (
                  <Card>
                    <Descriptions column={3} bordered size="small">
                      <Descriptions.Item label="开盘价">{quote.open}</Descriptions.Item>
                      <Descriptions.Item label="最高价">{quote.high}</Descriptions.Item>
                      <Descriptions.Item label="最低价">{quote.low}</Descriptions.Item>
                      <Descriptions.Item label="昨收">{quote.preClose}</Descriptions.Item>
                      <Descriptions.Item label="换手率">{quote.turnoverRate?.toFixed(2)}%</Descriptions.Item>
                      <Descriptions.Item label="量比">{quote.volumeRatio?.toFixed(2)}</Descriptions.Item>
                      <Descriptions.Item label="数据源">{quote.source}</Descriptions.Item>
                      <Descriptions.Item label="更新时间">{new Date(quote.timestamp).toLocaleString()}</Descriptions.Item>
                    </Descriptions>
                  </Card>
                ),
              },
            ]}
          />
        </>
      )}
    </div>
  );
};

export default StockDetail;
