import React from 'react';
import ReactECharts from 'echarts-for-react';
import { Empty } from 'antd';

interface Stock { code: string; name: string; industry?: string }
interface Props {
  concept: string;
  stocks: Stock[];
  onPick?: (code: string) => void;  // 点成分股节点回调（跳个股图谱）
}

/** 题材概念图：概念为中心，成分股放射。点成分股节点可联动看其关系图谱。 */
const ConceptGraph: React.FC<Props> = ({ concept, stocks, onPick }) => {
  if (!concept || stocks.length === 0) return <Empty description="选择概念查看成分股网络" />;

  const shown = stocks.slice(0, 40); // 限量防止节点过密
  const label = (s: Stock) => `${s.name || s.code}`;

  const nodes: any[] = [
    { name: concept, symbolSize: 60, category: 0, itemStyle: { color: '#fa541c' } },
    ...shown.map((s) => ({ name: label(s), symbolSize: 26, category: 1, code: s.code })),
  ];
  const links = shown.map((s) => ({ source: concept, target: label(s) }));

  const option = {
    tooltip: { formatter: (p: any) => (p.dataType === 'node' ? p.name : '') },
    legend: [{ data: ['题材', '成分股'], top: 0 }],
    series: [
      {
        type: 'graph',
        layout: 'force',
        roam: true,
        draggable: true,
        label: { show: true, fontSize: 10, position: 'right' },
        force: { repulsion: 170, edgeLength: 95, gravity: 0.08 },
        categories: [{ name: '题材' }, { name: '成分股' }],
        data: nodes,
        links,
        lineStyle: { color: '#d9d9d9', curveness: 0.08 },
        emphasis: { focus: 'adjacency' },
      },
    ],
  };

  const onEvents = {
    click: (p: any) => { if (p.data?.code && onPick) onPick(p.data.code); },
  };

  return <ReactECharts option={option} style={{ height: 460 }} notMerge onEvents={onEvents} />;
};

export default ConceptGraph;
