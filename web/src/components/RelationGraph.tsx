import React from 'react';
import ReactECharts from 'echarts-for-react';
import { Empty } from 'antd';

interface RelationItem { relatedCode: string; relationType: string }
interface CompanyItem { code: string; name: string }

interface Props {
  center: string;            // 中心公司（代码或名称）
  relations?: RelationItem[];
  suppliers?: CompanyItem[];
  customers?: CompanyItem[];
}

/**
 * 公司关系网络图（echarts 力导向）：中心公司 + 上游供应商 + 下游客户 + 关联方。
 */
const RelationGraph: React.FC<Props> = ({ center, relations = [], suppliers = [], customers = [] }) => {
  const hasData = relations.length + suppliers.length + customers.length > 0;
  if (!hasData) return <Empty description="暂无关系数据可视化" />;

  const nodes: any[] = [{ name: center, category: 0, symbolSize: 56, value: '本体' }];
  const links: any[] = [];
  const seen = new Set<string>([center]);

  const addNode = (label: string, category: number, size: number) => {
    if (!seen.has(label)) { nodes.push({ name: label, category, symbolSize: size }); seen.add(label); }
  };

  suppliers.forEach((s) => {
    const label = s.name || s.code;
    addNode(label, 1, 32);
    links.push({ source: label, target: center }); // 上游 → 本体
  });
  customers.forEach((c) => {
    const label = c.name || c.code;
    addNode(label, 2, 32);
    links.push({ source: center, target: label }); // 本体 → 下游
  });
  relations.forEach((r) => {
    addNode(r.relatedCode, 3, 26);
    links.push({ source: center, target: r.relatedCode, label: { show: true, formatter: r.relationType, fontSize: 10 } });
  });

  const option = {
    tooltip: {},
    legend: [{ data: ['本体', '上游供应商', '下游客户', '关联方'], top: 0 }],
    series: [
      {
        type: 'graph',
        layout: 'force',
        roam: true,
        draggable: true,
        label: { show: true, position: 'right', fontSize: 11 },
        force: { repulsion: 220, edgeLength: 130, gravity: 0.05 },
        categories: [
          { name: '本体' },
          { name: '上游供应商' },
          { name: '下游客户' },
          { name: '关联方' },
        ],
        data: nodes,
        links,
        lineStyle: { color: 'source', curveness: 0.12, opacity: 0.7 },
        edgeSymbol: ['none', 'arrow'],
        edgeSymbolSize: 8,
        emphasis: { focus: 'adjacency', lineStyle: { width: 3 } },
      },
    ],
  };

  return <ReactECharts option={option} style={{ height: 440 }} notMerge />;
};

export default RelationGraph;
