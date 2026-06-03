import React from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
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

function App() {
  return (
    <ConfigProvider locale={zhCN}>
      <ErrorBoundary>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<MainLayout />}>
              <Route index element={<Dashboard />} />
              <Route path="auto-trading" element={<AutoTrading />} />
              <Route path="positions" element={<Positions />} />
              <Route path="selection" element={<Selection />} />
              <Route path="selection-history" element={<SelectionHistory />} />
              <Route path="selection-config" element={<SelectionConfig />} />
              <Route path="sector" element={<Sector />} />
              <Route path="review" element={<Review />} />
              <Route path="agents" element={<Agents />} />
              <Route path="strategy" element={<Strategy />} />
              <Route path="knowledge" element={<KnowledgeGraph />} />
              <Route path="memory" element={<AgentMemory />} />
              <Route path="llm" element={<LLMManager />} />
              <Route path="observability" element={<Observability />} />
              <Route path="workflow" element={<WorkflowEditor />} />
              <Route path="stock-detail" element={<StockDetail />} />
              <Route path="ai-chat" element={<AIChat />} />
              <Route path="prompts" element={<PromptManager />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </ErrorBoundary>
    </ConfigProvider>
  );
}

export default App;
