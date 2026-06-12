import axios from 'axios';
import type {
  AgentStatus, AgentResult, WorkflowDefinition, WorkflowResult,
  DecisionResult, DecisionRequest, StockInfo, QuoteData,
  PositionSummary, OrderRequest, OrderResult,
  LLMModel, DeepSeekBalance, LLMTestResponse, ProviderStatus, AgentMemoryRecord,
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
export const getTradingStatus = () =>
  api.get<{ mode: string; halted: boolean; todayOrderCount: number }>('/execution/trading/status');
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
export const screenSelection = (criteria?: Record<string, unknown>, strategy?: string) =>
  api.post('/selection/screen', criteria || {}, { params: strategy ? { strategy } : {} });
// 重新选股并追加一条历史记录（不覆盖）
export const rerunSelection = (criteria?: Record<string, unknown>, strategy?: string) =>
  api.post('/selection/run', criteria || {}, { params: strategy ? { strategy } : {} });
// 可用选股策略清单
export interface StrategyInfo { key: string; name: string; description: string; preferredRegime: string; usesPatterns?: boolean; }
export const getStrategies = () => api.get<StrategyInfo[]>('/selection/strategies');
// 可选 K 线形态目录（供「K线形态」策略自选形态）
export interface PatternInfo { key: string; name: string; }
export const getSelectionPatterns = () => api.get<PatternInfo[]>('/selection/patterns');

// ============ 选股回测 ============
export interface BacktestTradeDto {
  code: string; name: string; signalDate: string; entryDate: string; entryPrice: number;
  exitDate: string; exitPrice: number; holdDays: number;
  returnPct: number; exitDeferred: boolean; maxRisePct: number; maxDropPct: number; win: boolean;
}
export interface BacktestReportDto {
  holdDays: number; entry: string;
  totalSignals: number; executedTrades: number; skippedNoData: number;
  skippedUntradable: number; deferredExits: number; frictionPct: number;
  winRatePct: number; avgReturnPct: number; medianReturnPct: number;
  profitFactor: number | null; stdDevPct: number; maxDrawdownPct: number;
  avgMaxRisePct: number; avgMaxDropPct: number; bestReturnPct: number; worstReturnPct: number;
  trades: BacktestTradeDto[];
}
export const getBacktest = (
  holdDays = 5, entry = 'NextOpen', from?: string, to?: string,
  tradability = true, friction = 0.3,
) =>
  api.get<BacktestReportDto>('/selection/backtest', {
    params: { holdDays, entry, from, to, tradability, friction },
  });
// 选股历史记录列表（元信息，按选股时间倒序）
export const getSelectionHistory = (take = 30) =>
  api.get('/selection/history', { params: { take } });
// 按 id 取某次选股的完整结果
export const getSelectionById = (id: number) => api.get(`/selection/history/${id}`);
// 某批选股的选后表现（次日/至今涨跌、最高涨幅、最低跌幅）
export const getSelectionPerformance = (id: number) => api.get(`/selection/history/${id}/performance`);
// 手动（重新）触发某批选股的 LLM 复评（异步）
export const reviewSelectionBatch = (id: number) => api.post(`/selection/history/${id}/review`);
export const getActivityPool = () => api.get('/selection/activity');

// ============ 策略记分板（选股信号前向绩效，Worker 每日补算） ============
export interface HorizonStatsDto {
  count: number; winRate: number | null; avgRet: number | null;
  avgExcess: number | null; profitFactor: number | null;
}
export interface ScoreboardSummaryDto {
  strategy: string; strategyName: string; signals: number; untradable: number; pending: number;
  horizon1: HorizonStatsDto; horizon3: HorizonStatsDto; horizon5: HorizonStatsDto;
}
export interface ScoreboardDetailDto {
  id: number; tradingDate: string; strategy: string; strategyName: string;
  code: string; name: string; score: number; signalClose: number;
  entryDate: string | null; entryPrice: number | null; untradable: boolean;
  ret1: number | null; ret3: number | null; ret5: number | null;
  excess1: number | null; excess3: number | null; excess5: number | null;
  status: string;
}
export const getScoreboardSummary = (days = 30) =>
  api.get<ScoreboardSummaryDto[]>('/selection/performance/summary', { params: { days } });
export const getScoreboardDetails = (strategy?: string, days = 30) =>
  api.get<ScoreboardDetailDto[]>('/selection/performance/details', { params: { strategy, days } });
// 手动触发物化+补算（补数/调试用，平时由 Worker selection-performance 任务定时执行）
export const syncScoreboard = () =>
  api.post<{ created: number; updated: number; finalized: number }>('/selection/performance/sync');

// ============ 选股配置中心（版本化阈值 + 权重）============
export interface SelectionWeights {
  capital: number; technical: number; position: number; form: number;
  dragonTiger: number; activity: number; theme: number; sector: number;
  regimeWeakFactor: number; regimeStrongFactor: number;
}
export interface SelectionCriteriaDto {
  topN: number;
  healthyRiseMin: number; healthyRiseMax: number;
  minVolumeRatio: number; shockAmplitude: number;
  minMainNetInflow: number; maxRsi: number; maxRise20d: number;
  requireDragonTiger: boolean;
  maxTotalMarketCap: number; excludeTraditionalIndustry: boolean;
  excludeIndustryKeywords: string[];
  useLlmNarrative: boolean;
  weights: SelectionWeights;
}
export interface SelectionConfigItem {
  id: number; name: string; version: string; configJson: string;
  isActive: boolean; remark?: string; createdAt: string; updatedAt: string;
}
export const getSelectionConfig = (name?: string) =>
  api.get<SelectionCriteriaDto>('/selection/config', { params: name ? { name } : {} });
export const getSelectionConfigDefault = () =>
  api.get<SelectionCriteriaDto>('/selection/config/default');
export const listSelectionConfig = () =>
  api.get<SelectionConfigItem[]>('/selection/config/list');
export const saveSelectionConfig = (body: {
  name: string; version: string; remark?: string; activate: boolean; criteria: SelectionCriteriaDto;
}) => api.post('/selection/config', body);
export const activateSelectionConfig = (id: number) =>
  api.post(`/selection/config/${id}/activate`);
export const deleteSelectionConfig = (id: number) =>
  api.delete(`/selection/config/${id}`);

// ============ 自建策略管理（strategy_definition 口径定义）============
export interface StrategyFilters {
  byRsi: boolean; byRise20d: boolean; byMinInflow: boolean;
  requireAboveMa20: boolean; requireHotConcept: boolean;
  excludeTraditionalBigCap: boolean; extremeRise20d: number | null;
  weakRegimeTightenRise20d: boolean; weakRegimeRequireInflow: boolean;
  requirePatterns?: string[]; requireAllPatterns?: boolean;
}
export interface StrategyFactorKinds { technical: 'lowdip' | 'trend'; position: 'lowdip' | 'trend'; }
export interface StrategyDefinition {
  key: string; name: string; description: string; preferredRegime: string;
  filters: StrategyFilters; factorKinds: StrategyFactorKinds;
  penalty: 'none' | 'limitup'; coreLogicTemplate?: string | null;
  scanFullUniverse?: boolean;
}
export interface StrategyDefItem {
  id: number; enabled: boolean; createdAt: string; updatedAt: string;
  definition: StrategyDefinition;
}
export const listStrategyDefs = () => api.get<StrategyDefItem[]>('/strategy-def');
export const getStrategyDefBuiltins = () =>
  api.get<StrategyDefinition[]>('/strategy-def/builtins');
export const createStrategyDef = (body: { enabled: boolean; definition: StrategyDefinition }) =>
  api.post('/strategy-def', body);
export const updateStrategyDef = (id: number, body: { enabled: boolean; definition: StrategyDefinition }) =>
  api.put(`/strategy-def/${id}`, body);
export const deleteStrategyDef = (id: number) =>
  api.delete(`/strategy-def/${id}`);

// ============ 每日复盘 ============
export const getLatestReview = () => api.get('/review/latest');
export const getReviewByDate = (date: string) => api.get(`/review/${date}`);
export const runReview = (date?: string) => api.post('/review/run', null, { params: date ? { date } : {} });
export const getReviewHistory = (take = 30) => api.get('/review/history', { params: { take } });

// ============ 板块资金流 ============
export interface SectorFlow {
  sectorCode: string; sectorName: string; changePercent: number;
  netInflow: number; price: number; turnoverAmount: number;
}
export const getSectorRanking = (direction: 'inflow' | 'outflow' = 'inflow') =>
  api.get<SectorFlow[]>('/sector/ranking', { params: { direction } });
export const getSectorStrongStocks = (sectorCode: string, top = 10) =>
  api.get(`/sector/${sectorCode}/strong-stocks`, { params: { top } });

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
export const getConcepts = (top = 60) =>
  api.get<{ concept: string; stockCount: number }[]>('/knowledge/concepts', { params: { top } });
export const getConceptStocks = (name: string) =>
  api.get<{ code: string; name: string; industry: string }[]>(`/knowledge/concept/${encodeURIComponent(name)}/stocks`);
export interface CandidateEdge {
  from: string; to: string; edgeType: string;
  credibility: number; mentionCount: number; promoted: boolean; sourceUrl?: string;
}
export const getCandidateEdges = (edgeType?: string, entity?: string, top = 300) =>
  api.get<CandidateEdge[]>('/knowledge/candidate-edges', { params: { edgeType, entity, top } });
export const getCandidateValues = (edgeType: string, top = 0) =>
  api.get<{ value: string; count: number }[]>('/knowledge/candidate-edges/values', { params: { edgeType, top } });

// ============ LLM ============
export const getLLMModels = () => api.get<LLMModel[]>('/llm/models');
export const addLLMModel = (model: LLMModel) => api.post<LLMModel>('/llm/models', model);
export const updateLLMModel = (modelId: string, model: LLMModel) => api.put<LLMModel>(`/llm/models/${modelId}`, model);
export const deleteLLMModel = (modelId: string) => api.delete(`/llm/models/${modelId}`);
export const refreshLLMModels = () => api.post('/llm/models/refresh');
export const getDeepSeekBalance = (modelId: string) =>
  api.get<DeepSeekBalance>(`/llm/models/${modelId}/balance`);
export const testLLMModel = (modelId: string, userPrompt?: string, systemPrompt?: string) =>
  api.post<LLMTestResponse>(`/llm/models/${modelId}/test`, { userPrompt, systemPrompt });
export const testLLMConfig = (config: LLMModel, userPrompt?: string, systemPrompt?: string) =>
  api.post<LLMTestResponse>('/llm/models/test', { config, userPrompt, systemPrompt });
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

// ============ Worker 任务配置（前端可配置，热生效） ============
export interface WorkerJobConfig {
  name: string;
  displayName: string;
  dynamic: boolean;
  hint: string;
  enabled: boolean;
  runOnStartup: boolean;
  intervalSeconds: number;
  dailyAtHour: number;
  dailyAtMinute: number;
}
export interface WorkerSectionMeta { section: string; displayName: string; }
export interface WorkerJobStatus {
  name: string;
  isRunning: boolean;
  lastStart: string | null;
  lastEnd: string | null;
  lastDurationMs: number | null;
  lastTrigger: string | null;
  lastSuccess: boolean | null;
  lastError: string | null;
  runRequested: boolean;
}
export const getWorkerJobs = () => api.get<WorkerJobConfig[]>('/workerconfig/jobs');
export const saveWorkerJobs = (jobs: WorkerJobConfig[]) => api.put('/workerconfig/jobs', jobs);
export const getWorkerJobsStatus = () => api.get<WorkerJobStatus[]>('/workerconfig/jobs/status');
export const runWorkerJob = (name: string) =>
  api.post<{ message: string; running: boolean }>(`/workerconfig/jobs/${name}/run`);
export const listWorkerSections = () => api.get<WorkerSectionMeta[]>('/workerconfig/sections');
export const getWorkerSection = (section: string) =>
  api.get<Record<string, unknown>>(`/workerconfig/sections/${section}`);
export const saveWorkerSection = (section: string, body: Record<string, unknown>) =>
  api.put(`/workerconfig/sections/${section}`, body);

export default api;
