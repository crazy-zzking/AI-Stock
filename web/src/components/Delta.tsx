import React from 'react';
import { pct, yi, num } from '../utils/format';
import { useThemeToken } from '../theme/useThemeToken';

interface DeltaProps {
  value?: number | null;
  /** pct=涨跌幅% / money=亿万金额 / raw=普通数值 */
  mode?: 'pct' | 'money' | 'raw';
  digits?: number;
  bold?: boolean;
  /** 是否按涨跌上色（默认 true）。money 类常需上色，raw 可关 */
  colored?: boolean;
}

/** 行情数字：红涨绿跌 + 等宽对齐，统一格式化 */
const Delta: React.FC<DeltaProps> = ({ value, mode = 'pct', digits = 2, bold, colored = true }) => {
  const { upColor, downColor, token } = useThemeToken();
  const text = mode === 'pct' ? pct(value, digits) : mode === 'money' ? yi(value) : num(value, digits);
  const isUp = (value ?? 0) >= 0;

  return (
    <span
      style={{
        color: colored ? (isUp ? upColor : downColor) : undefined,
        fontWeight: bold ? 600 : undefined,
        fontVariantNumeric: 'tabular-nums',
        fontFamily: token.fontFamilyCode,
      }}
    >
      {text}
    </span>
  );
};

export default Delta;
