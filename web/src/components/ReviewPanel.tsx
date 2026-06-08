import React from 'react';
import { Tag, Tooltip } from 'antd';
import type { LlmReview } from '../types/models';

/** 建议等级 → 展示文案/颜色（0=回避 1=观望 2=建议买入） */
const REC = {
  2: { text: '建议买入', color: '#cf1322', bg: '#fff1f0', border: '#ffa39e' },
  1: { text: '观望', color: '#8c8c8c', bg: '#fafafa', border: '#d9d9d9' },
  0: { text: '回避', color: '#389e0d', bg: '#f6ffed', border: '#b7eb8f' },
} as const;

const f2 = (v?: number) => (v == null ? '—' : v.toFixed(2));

/**
 * 选股 LLM 复评展示面板：建议徽标 + 置信度 + 风险标签 + 情报印证 + 核心逻辑 + 买入计划。
 * review 为空时渲染占位提示（未复评/不在复评范围）。
 */
const ReviewPanel: React.FC<{ review?: LlmReview | null; compact?: boolean }> = ({ review, compact }) => {
  if (!review) {
    return <div style={{ color: '#bbb', fontSize: 12 }}>· 暂无 LLM 复评</div>;
  }
  const rec = REC[review.recommendation] ?? REC[1];
  const plan = review.plan;

  return (
    <div
      style={{
        border: `1px solid ${rec.border}`,
        background: rec.bg,
        borderRadius: 6,
        padding: compact ? '6px 10px' : '8px 12px',
        fontSize: 13,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 4 }}>
        <span style={{ fontWeight: 700, color: rec.color }}>🤖 LLM：{rec.text}</span>
        <span style={{ color: '#999', fontSize: 12 }}>置信 {review.confidence}</span>
        {review.model && <span style={{ color: '#bbb', fontSize: 12 }}>· {review.model}</span>}
        {(review.riskFlags?.length ?? 0) > 0 &&
          review.riskFlags.map((r) => <Tag color="volcano" key={r} style={{ margin: 0 }}>⚠ {r}</Tag>)}
      </div>

      {review.narrative && (
        <div style={{ color: '#555', marginBottom: 4 }}>
          <b>看点：</b>{review.narrative}
        </div>
      )}
      {review.intelligenceNote && (
        <div style={{ color: '#555', marginBottom: plan ? 4 : 0 }}>
          <b>情报印证：</b>{review.intelligenceNote}
        </div>
      )}

      {plan && (
        <div
          style={{
            marginTop: 4, paddingTop: 6, borderTop: '1px dashed #ddd',
            display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'center',
          }}
        >
          <span>买入区间 <b style={{ color: '#cf1322' }}>{f2(plan.buyLow)}~{f2(plan.buyHigh)}</b></span>
          <span>止损 <b style={{ color: '#389e0d' }}>{f2(plan.stopLoss)}</b></span>
          <span>止盈 <b style={{ color: '#cf1322' }}>{f2(plan.takeProfit)}</b></span>
          {plan.basis && (
            <Tooltip title={plan.basis}>
              <span style={{ color: '#999', fontSize: 12, cursor: 'help' }}>依据ⓘ</span>
            </Tooltip>
          )}
        </div>
      )}
    </div>
  );
};

export default ReviewPanel;
