// 统一的行情数字格式化工具（红涨绿跌、亿/万、百分比）

import { UP_COLOR, DOWN_COLOR } from '../theme/tokens';

/** 涨跌幅/百分比：带正负号，null 显示 — */
export const pct = (v?: number | null, digits = 2): string =>
  v == null ? '—' : `${v >= 0 ? '+' : ''}${v.toFixed(digits)}%`;

/** A股习惯：红涨绿跌；0 视为涨色 */
export const upDownColor = (v?: number | null): string => ((v ?? 0) >= 0 ? UP_COLOR : DOWN_COLOR);

/** 金额：亿/万 自适应 */
export const yi = (v?: number | null): string => {
  const x = v ?? 0;
  const abs = Math.abs(x);
  if (abs >= 1e8) return `${(x / 1e8).toFixed(2)}亿`;
  if (abs >= 1e4) return `${(x / 1e4).toFixed(0)}万`;
  return `${x.toFixed(0)}`;
};

/** 普通数值，null 显示 — */
export const num = (v?: number | null, digits = 2): string => (v == null ? '—' : v.toFixed(digits));
