import React from 'react';
import { Tag, Tooltip } from 'antd';
import type { LlmReview } from '../types/models';
import { useThemeToken } from '../theme/useThemeToken';
import { UP_COLOR, DOWN_COLOR, UP_BG, UP_BORDER, DOWN_BG, DOWN_BORDER, NEUTRAL_COLOR, NEUTRAL_BG, NEUTRAL_BORDER } from '../theme/tokens';

/** 建议等级 → 展示文案/颜色（0=回避 1=观望 2=建议买入） */
const REC_BASE = {
  2: { text: '建议买入', color: UP_COLOR, bg: UP_BG, border: UP_BORDER },
  1: { text: '观望', color: NEUTRAL_COLOR, bg: NEUTRAL_BG, border: NEUTRAL_BORDER },
  0: { text: '回避', color: DOWN_COLOR, bg: DOWN_BG, border: DOWN_BORDER },
} as const;

const f2 = (v?: number) => (v == null ? '—' : v.toFixed(2));

/**
 * 选股 LLM 复评展示面板：建议徽标 + 置信度 + 风险标签 + 情报印证 + 核心逻辑 + 买入计划。
 * review 为空时渲染占位提示（未复评/不在复评范围）。
 */
const ReviewPanel: React.FC<{ review?: LlmReview | null; compact?: boolean }> = ({ review, compact }) => {
  const { token, dark, upColor, downColor, upBg, downBg, neutralBg } = useThemeToken();

  // 暗色模式下调整建议色
  const REC = dark
    ? {
        2: { ...REC_BASE[2], color: '#ff7875', bg: '#2a1215', border: '#a8071a' },
        1: { ...REC_BASE[1], color: '#bbb', bg: '#1a1a1a', border: '#434343' },
        0: { ...REC_BASE[0], color: '#73d13d', bg: '#162312', border: '#237804' },
      } as const
    : REC_BASE;

  if (!review) {
    return <div style={{ color: token.colorTextQuaternary, fontSize: 12 }}>· 暂无 LLM 复评</div>;
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
        <span style={{ color: token.colorTextSecondary, fontSize: 12 }}>置信 {review.confidence}</span>
        {review.model && <span style={{ color: token.colorTextQuaternary, fontSize: 12 }}>· {review.model}</span>}
        {(review.riskFlags?.length ?? 0) > 0 &&
          review.riskFlags.map((r) => <Tag color="volcano" key={r} style={{ margin: 0 }}>⚠ {r}</Tag>)}
      </div>

      {review.narrative && (
        <div style={{ color: token.colorText, marginBottom: 4 }}>
          <b>看点：</b>{review.narrative}
        </div>
      )}
      {review.intelligenceNote && (
        <div style={{ color: token.colorText, marginBottom: plan ? 4 : 0 }}>
          <b>情报印证：</b>{review.intelligenceNote}
        </div>
      )}

      {plan && (
        <div
          style={{
            marginTop: 4, paddingTop: 6, borderTop: `1px dashed ${token.colorBorderSecondary}`,
            display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'center',
          }}
        >
          <span>买入区间 <b style={{ color: upColor }}>{f2(plan.buyLow)}~{f2(plan.buyHigh)}</b></span>
          <span>止损 <b style={{ color: downColor }}>{f2(plan.stopLoss)}</b></span>
          <span>止盈 <b style={{ color: upColor }}>{f2(plan.takeProfit)}</b></span>
          {plan.basis && (
            <Tooltip title={plan.basis}>
              <span style={{ color: token.colorTextSecondary, fontSize: 12, cursor: 'help' }}>依据ⓘ</span>
            </Tooltip>
          )}
        </div>
      )}
    </div>
  );
};

export default ReviewPanel;
