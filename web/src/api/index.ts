import axios from 'axios';
import type {
  AgentStatus, AgentResult, WorkflowDefinition, WorkflowResult,
  DecisionResult, DecisionRequest, StockInfo, QuoteData,
  PositionSummary, OrderRequest, OrderResult,
  LLMModel, ProviderStatus, AgentMemoryRecord,
  PromptInfo, PromptTemplate,
} from '../types/models';

const api = axios.create({
  baseURL: process.env.REACT_APP_API_BASE_URL || 'http://localhost:5000/api',
  timeout: 30000,
});

// ============ 健康检查 ============
export const getHealth = () => api.get('/health');

// ============ Agent 编排 ============
export const getAgents = () => api.get<AgentStatus[]>('/orchestrator/agents');
export const analyzeStock = (code: string) =>
  api.post<AgentResult>('/orchestrator/analyze', { code });
export const generateSignal = (code: string) =>
  api.post<AgentResult>('/orchestrator/signal', { code });
export const executeWorkflow = (workflow: WorkflowDefinition) =>
  api.post<WorkflowResult>('/orchestrator/workflow', workflow);
export const makeDecision = (code: string, totalCapital?: number) =>
  api.post<DecisionResult>('/orchestrator/decision', { code, totalCapital } as DecisionRequest);
export const makeBatchDecision = (codes: string[], totalCapital: number) =>
  api.post<DecisionResult[]>('/orchestrator/decision/batch', { codes, totalCapital });

// ============ 股票数据 ============
export const getStockList = () => api.get<StockInfo[]>('/stock/list');
export const getQuote = (code: string, provider?: string) =>
  api.get<QuoteData>(`/stock/${code}/quote`, { params: { provider } });
export const getQuotes = (codes: string[]) =>
  api.post<QuoteData[]>('/stock/quotes', codes);
export const getKlines = (code: string, interval = 'Daily', count = 100) =>
  api.get(`/stock/${code}/kline`, { params: { interval, count } });
export const getProviderStatus = () =>
  api.get<ProviderStatus[]>('/stock/providers/status');

// ============ 交易执行 ============
export const getPositions = () => api.get<PositionSummary>('/execution/positions');
export const refreshPositions = () => api.post<PositionSummary>('/execution/positions/refresh');
export const getPosition = (code: string) =>
  api.get('/execution/positions/' + code);
export const placeOrder = (order: OrderRequest) =>
  api.post<OrderResult>('/execution/order', order);
export const getOrders = (startTime: string, endTime: string) =>
  api.get('/execution/orders', { params: { startTime, endTime } });

// ============ 特征工程 ============
export const getMarketState = () => api.get<number>('/feature/market/state');
export const getMarketSentiment = () => api.get('/feature/market/sentiment');
export const getIndicators = (code: string) => api.get(`/feature/${code}/indicators`);

// ============ 策略 ============
export const runBacktest = (config: Record<string, unknown>) =>
  api.post('/strategy/backtest', config);

// ============ 风控 ============
export const checkRisk = (data: Record<string, unknown>) =>
  api.post('/risk/check', data);

// ============ 选股 ============
export const getLatestSelection = (topN = 5) =>
  api.get(`/selection/latest`, { params: { topN } });
export const screenSelection = (criteria?: Record<string, unknown>) =>
  api.post('/selection/screen', criteria || {});
export const getActivityPool = () => api.get('/selection/activity');

// ============ 知识图谱 ============
export const getChains = () => api.get<string[]>('/knowledge/chains');
export const getChainStructure = (chainName: string) =>
  api.get(`/knowledge/chain/${chainName}`);
export const getCompanyRelations = (companyCode: string) =>
  api.get(`/knowledge/company/${companyCode}/relations`);
export const getSuppliers = (companyCode: string) =>
  api.get(`/knowledge/company/${companyCode}/suppliers`);
export const getCustomers = (companyCode: string) =>
  api.get(`/knowledge/company/${companyCode}/customers`);
export const getChainCompanies = (chainName: string, role?: string) =>
  api.get(`/knowledge/chain/${chainName}/companies`, { params: { role } });
export const findRelationPath = (from: string, to: string, maxDepth = 3) =>
  api.get('/knowledge/company/path', { params: { from, to, maxDepth } });
export const diffuseConcept = (coreEvent: string, relatedConcepts: string[]) =>
  api.post('/knowledge/chain/diffuse', { coreEvent, relatedConcepts });

// ============ LLM ============
export const getLLMModels = () => api.get<LLMModel[]>('/llm/models');
export const addLLMModel = (model: LLMModel) => api.post<LLMModel>('/llm/models', model);
export const updateLLMModel = (modelId: string, model: LLMModel) => api.put<LLMModel>(`/llm/models/${modelId}`, model);
export const deleteLLMModel = (modelId: string) => api.delete(`/llm/models/${modelId}`);
export const refreshLLMModels = () => api.post('/llm/models/refresh');
export const sendChat = (userPrompt: string, systemPrompt?: string, modelId?: string) =>
  api.post('/llm/chat', { userPrompt, systemPrompt, modelId });
export const compareLLM = (prompt: string, modelCount = 3, systemPrompt?: string) =>
  api.post('/llm/compare', { prompt, modelCount, systemPrompt });

// ============ Prompt ============
export const getPrompts = (category?: string) =>
  api.get<PromptInfo[]>('/prompt', { params: { category } });
export const getPromptDetail = (name: string, version?: string) =>
  api.get<PromptTemplate>(`/prompt/${name}`, { params: { version } });
export const savePrompt = (template: PromptTemplate) =>
  api.post('/prompt', template);
export const deletePrompt = (name: string, version: string) =>
  api.delete(`/prompt/${name}`, { params: { version } });
export const reloadPrompts = () => api.post('/prompt/reload');

// ============ Agent Memory (需后端补充端点) ============
// 使用现有 orchestrator 端点暂时替代
export const getAgentMemoryHistory = (agentId: string, stockCode: string, count = 10) =>
  api.get<AgentMemoryRecord[]>(`/orchestrator/memory/${agentId}/${stockCode}`, { params: { count } });

export default api;
