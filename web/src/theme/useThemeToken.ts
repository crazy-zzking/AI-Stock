/**
 * useThemeToken — 组件级主题 Token 访问
 *
 * 提供：antd Design Token + AI-Stock 自定义语义色。
 * 暗色模式下 token.colorError/colorSuccess 会自动跟随 antd 算法推导，
 * 而涨跌语义色（UP/DOWN）在暗色模式下需要手动调整以保证可读性。
 */
import { theme } from 'antd';
import { useThemeMode } from '../contexts/ThemeContext';
import { UP_COLOR, DOWN_COLOR, UP_BG, DOWN_BG, NEUTRAL_BG, SIDER_DARK_BG } from './tokens';

export function useThemeToken() {
  const { token } = theme.useToken();
  const { dark } = useThemeMode();

  return {
    /** antd 原始 token */
    token,

    /** 暗色模式标志 */
    dark,

    // ---- 语义色（暗色模式下自动调亮） ----

    upColor: dark ? '#ff7875' : UP_COLOR,
    downColor: dark ? '#73d13d' : DOWN_COLOR,
    upBg: dark ? '#2a1215' : UP_BG,
    downBg: dark ? '#162312' : DOWN_BG,
    neutralBg: dark ? '#1a1a1a' : NEUTRAL_BG,
    siderBg: dark ? SIDER_DARK_BG : SIDER_DARK_BG,

    // ---- 语义色在 antd token 上的别名 ----

    colorUp: token.colorError,
    colorDown: token.colorSuccess,
  };
}
