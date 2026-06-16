import React from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { ConfigProvider, theme } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { ThemeProvider, useThemeMode } from './contexts/ThemeContext';
import { BRAND_PRIMARY, UP_COLOR, DOWN_COLOR, FONT_MONO } from './theme/tokens';
import ErrorBoundary from './components/ErrorBoundary';
import MainLayout from './layouts/MainLayout';
import Dashboard from './pages/Dashboard';
import Positions from './pages/Positions';
import Selection from './pages/Selection';
import Sector from './pages/Sector';
import Review from './pages/Review';
import SelectionHistory from './pages/SelectionHistory';
import Agents from './pages/Agents';
import Strategy from './pages/Strategy';
import AutoTrading from './pages/AutoTrading';
import KnowledgeGraph from './pages/KnowledgeGraph';
import AgentMemory from './pages/AgentMemory';
import LLMManager from './pages/LLMManager';
import Observability from './pages/Observability';
import WorkflowEditor from './pages/WorkflowEditor';
import StockDetail from './pages/StockDetail';
import AIChat from './pages/AIChat';
import PromptManager from './pages/PromptManager';
import SelectionConfig from './pages/SelectionConfig';
import StrategyManager from './pages/StrategyManager';
import Backtest from './pages/Backtest';
import WorkerConfig from './pages/WorkerConfig';
import StrategyScoreboard from './pages/StrategyScoreboard';
import ReplayBacktest from './pages/ReplayBacktest';
import Intelligence from './pages/Intelligence';
import TradeCandidates from './pages/TradeCandidates';

function AppShell() {
  const { dark } = useThemeMode();
  return (
    <ConfigProvider
      locale={zhCN}
      theme={{
        algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
        token: {
          // 品牌色
          colorPrimary: BRAND_PRIMARY,
          // A股涨跌色：红涨绿跌（已反直觉映射到 error/success）
          colorError: UP_COLOR,
          colorSuccess: DOWN_COLOR,
          // 圆角
          borderRadius: 6,
          // 等宽数字字体
          fontFamilyCode: FONT_MONO,
          // 轻微降低默认字号，适配数据密集场景
          fontSize: 14,
        },
      }}
    >
      <ErrorBoundary>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<MainLayout />}>
              <Route index element={<Dashboard />} />
              <Route path="trade-candidates" element={<TradeCandidates />} />
              <Route path="auto-trading" element={<AutoTrading />} />
              <Route path="positions" element={<Positions />} />
              <Route path="selection" element={<Selection />} />
              <Route path="selection-history" element={<SelectionHistory />} />
              <Route path="selection-config" element={<SelectionConfig />} />
              <Route path="strategy-manager" element={<StrategyManager />} />
              <Route path="selection-backtest" element={<Backtest />} />
              <Route path="strategy-scoreboard" element={<StrategyScoreboard />} />
              <Route path="replay-backtest" element={<ReplayBacktest />} />
              <Route path="sector" element={<Sector />} />
              <Route path="review" element={<Review />} />
              <Route path="agents" element={<Agents />} />
              <Route path="strategy" element={<Strategy />} />
              <Route path="knowledge" element={<KnowledgeGraph />} />
              <Route path="intelligence" element={<Intelligence />} />
              <Route path="memory" element={<AgentMemory />} />
              <Route path="llm" element={<LLMManager />} />
              <Route path="observability" element={<Observability />} />
              <Route path="workflow" element={<WorkflowEditor />} />
              <Route path="stock-detail" element={<StockDetail />} />
              <Route path="ai-chat" element={<AIChat />} />
              <Route path="prompts" element={<PromptManager />} />
              <Route path="worker-config" element={<WorkerConfig />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </ErrorBoundary>
    </ConfigProvider>
  );
}

function App() {
  return (
    <ThemeProvider>
      <AppShell />
    </ThemeProvider>
  );
}

export default App;
