/**
 * AI-Stock 统一设计 Token
 *
 * 所有组件中散落的硬编码色值逐步收敛至此文件，通过 antd Design Token 或常量引用。
 * 暗色模式：ConfigProvider 自动推导；需要手动适配的 Token 通过 useThemeToken() hook 获取。
 */

// ============================================================
//  品牌 & 语义色
// ============================================================

/** 品牌主色 — 海军蓝，比 antd 默认蓝更沉稳，适配金融场景 */
export const BRAND_PRIMARY = '#3182CE';

/** A股涨 / 买入 — 红色 */
export const UP_COLOR = '#cf1322';
export const UP_BG = '#fff1f0';
export const UP_BORDER = '#ffa39e';

/** A股跌 / 卖出 — 绿色 */
export const DOWN_COLOR = '#3f8600';
export const DOWN_BG = '#f6ffed';
export const DOWN_BORDER = '#b7eb8f';

/** 观望 / 中性 */
export const NEUTRAL_COLOR = '#8c8c8c';
export const NEUTRAL_BG = '#fafafa';
export const NEUTRAL_BORDER = '#d9d9d9';

// ============================================================
//  中性色（亮色模式基准，暗色由 antd darkAlgorithm 自动推导）
// ============================================================

/** 侧栏暗色背景 */
export const SIDER_DARK_BG = '#0B1121';

/** 卡片 / 内容区背景 */
export const BG_LAYOUT = '#F8FAFC';

// ============================================================
//  字体
// ============================================================

/** 等宽数字字体（行情数据专用） */
export const FONT_MONO = "'JetBrains Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace";

// ============================================================
//  间距（8pt 网格基准）
// ============================================================

export const SPACING = {
  xs: 4,
  sm: 8,
  md: 12,
  base: 16,
  lg: 20,
  xl: 24,
  xxl: 32,
  xxxl: 48,
} as const;

// ============================================================
//  圆角
// ============================================================

export const RADIUS = {
  sm: 4,
  base: 6,
  lg: 8,
  xl: 12,
} as const;
