import React from 'react';
import ReactECharts from 'echarts-for-react';
import { Empty } from 'antd';
import type { CandidateEdge } from '../api';

interface Props {
  edges: CandidateEdge[];
  onPick?: (entity: string) => void;
}

/**
 * 知识图谱候选边可视化（graph_candidate_edge）：
 * 节点=实体（公司/概念），边=情报推断的候选关系。
 * 已晋升=绿色实线，候选中=灰色虚线；线越粗提及越多；tooltip 显示可信度/提及。
 */
const CandidateGraph: React.FC<Props> = ({ edges, onPick }) => {
  if (!edges || edges.length === 0) {
    return <Empty description="暂无候选边数据（需情报采集 + 小作文/共现抽取入 graph_candidate_edge）" />;
  }

  // concept 边的 to 是概念名，作概念节点；其余作公司节点
  const conceptSet = new Set(edges.filter((e) => e.edgeType === 'concept').map((e) => e.to));
  const nodeMap = new Map<string, any>();
  const ensureNode = (name: string) => {
    if (!nodeMap.has(name)) {
      const isConcept = conceptSet.has(name);
      nodeMap.set(name, { name, symbolSize: isConcept ? 42 : 24, category: isConcept ? 1 : 0 });
    }
  };
  edges.forEach((e) => { ensureNode(e.from); ensureNode(e.to); });

  const links = edges.map((e) => ({
    source: e.from,
    target: e.to,
    info: `${e.edgeType}｜提及${e.mentionCount}｜可信${e.credibility}${e.promoted ? '｜已晋升' : '｜候选中'}`,
    lineStyle: {
      width: Math.min(1 + e.mentionCount, 6),
      color: e.promoted ? '#52c41a' : '#bfbfbf',
      type: e.promoted ? 'solid' : 'dashed',
      opacity: 0.85,
      curveness: 0.08,
    },
  }));

  const option = {
    tooltip: {
      formatter: (p: any) =>
        p.dataType === 'edge' ? `${p.data.source} → ${p.data.target}<br/>${p.data.info}` : p.name,
    },
    legend: [{ data: ['公司', '概念'], top: 0 }],
    series: [
      {
        type: 'graph',
        layout: 'force',
        roam: true,
        draggable: true,
        label: { show: true, fontSize: 10, position: 'right' },
        force: { repulsion: 190, edgeLength: 115, gravity: 0.06 },
        categories: [{ name: '公司' }, { name: '概念' }],
        data: Array.from(nodeMap.values()),
        links,
        emphasis: { focus: 'adjacency' },
      },
    ],
  };

  const onEvents = {
    click: (p: any) => { if (p.dataType === 'node' && onPick) onPick(p.name); },
  };

  return <ReactECharts option={option} style={{ height: 520 }} notMerge onEvents={onEvents} />;
};

export default CandidateGraph;
