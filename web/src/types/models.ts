// ============================================================
// 业务模型类型定义 — 与后端 C# 模型对应
// ============================================================

// --- Agent 相关 ---
export interface AgentStatus {
  agentId: string;
  isOnline: boolean;
  currentTasks: number;
  completedTasks: number;
  lastActiveTime: string;
}

export interface AgentTask {
  taskType: string;
  parameters: Record<string, unknown>;
}

export interface AgentResult {
  success: boolean;
  output: Record<string, unknown>;
  message: string;
  executionTime: number;
}

// --- Workflow ---
export interface WorkflowStep {
  stepId: string;
  agentId: string;
  taskType: string;
  parameters: Record<string, unknown>;
  dependsOn: string[];
}

export interface WorkflowDefinition {
  workflowId: string;
  name: string;
  steps: WorkflowStep[];
}

export interface WorkflowResult {
  workflowId: string;
  success: boolean;
  stepResults: Record<string, AgentResult>;
  finalOutput: Record<string, unknown>;
  totalExecutionTime: number;
}

// --- 交易信号 ---
export interface TradeSignal {
  signalId: string;
  code: string;
  signalType: number; // 0=Buy, 1=Sell, 2=Hold
  strength: number;
  price: number;
  volume: number;
  strategyName: string;
  reason: string;
  signalTime: string;
  stopLossPrice: number | null;
  takeProfitPrice: number | null;
}

export interface SignalResult {
  code: string;
  currentPrice: number;
  signals: TradeSignal[];
}

// --- 风控 ---
export interface RiskCheckItem {
  riskCheckResult: unknown;
}

export interface RiskCheckResult {
  checks: RiskCheckItem[];
}

// --- 订单 ---
export interface OrderRequest {
  code: string;
  side: string;
  orderType: number;
  price: number;
  volume: number;
  strategyName: string;
  signalId: string;
}

export interface OrderResult {
  orderId: string;
  success: boolean;
  message: string;
  status: number;
}

// --- 决策 ---
export interface DecisionResult {
  code: string;
  success: boolean;
  analysis: Record<string, unknown>;
  signals: SignalResult;
  riskCheck: RiskCheckResult;
  orders: OrderResult[];
  message: string;
  executionTime: number;
}

export interface DecisionRequest {
  code: string;
  totalCapital?: number;
}

// --- 股票 ---
export interface StockInfo {
  code: string;
  name: string;
  market: string;
  industry: string;
  listDate: string | null;
  isDelisted: boolean;
}

export interface KlineData {
  code: string;
  dateTime: string;
  open: number;
  close: number;
  high: number;
  low: number;
  volume: number;
  amount: number;
  turnoverRate: number;
  changePercent: number;
  source: string;
}

export interface QuoteData {
  code: string;
  name: string;
  price: number;
  preClose: number;
  open: number;
  high: number;
  low: number;
  volume: number;
  amount: number;
  changePercent: number;
  changeAmount: number;
  turnoverRate: number;
  volumeRatio: number;
  timestamp: string;
  source: string;
}

// --- 持仓 ---
export interface PositionItem {
  code: string;
  name: string;
  volume: number;
  costPrice: number;
  currentPrice: number;
  marketValue: number;
  profit: number;
  profitRate: number;
}

export interface PositionSummary {
  totalAssets: number;
  availableBalance: number;
  positionValue: number;
  totalProfit: number;
  totalProfitRate: number;
  positionCount: number;
  profitCount: number;
  lossCount: number;
  updatedAt: string | null;
  positions: PositionItem[];
}

// --- LLM ---
export interface LLMModel {
  id: string;
  name: string;
  baseUrl: string;
  model: string;
  isEnabled: boolean;
  priority: number;
  timeoutSeconds: number;
  maxTokens: number;
  temperature: number;
  description: string;
  enableThinking?: boolean;
  thinkingBudgetTokens?: number;
  supportsMultimodal?: boolean;
}

export interface DeepSeekBalanceInfo {
  currency: string;
  totalBalance: string;
  grantedBalance: string;
  toppedUpBalance: string;
}

export interface DeepSeekBalance {
  success: boolean;
  isAvailable: boolean;
  balanceInfos: DeepSeekBalanceInfo[];
  errorMessage?: string;
}

// --- 知识图谱 ---
export interface CompanyRelation {
  companyCode: string;
  relatedCode: string;
  relationType: string;
  weight: number;
}

export interface GraphContext {
  companyInfo: string | null;
  suppliers: string | null;
  customers: string | null;
  industryChain: string | null;
  relatedEvents: string | null;
  relationPaths: string | null;
}

// --- 数据源 ---
export interface ProviderStatus {
  providerId: string;
  providerName: string;
  isHealthy: boolean;
  capabilities: number[];
}

// --- Agent Memory ---
export interface AgentMemoryRecord {
  id: number;
  agentId: string;
  taskType: string;
  stockCode: string;
  input: string;
  output: string;
  keyMetrics: string | null;
  success: boolean;
  message: string;
  executionTimeMs: number;
  createdAt: string;
}

// --- Prompt ---
export interface PromptInfo {
  name: string;
  version: string;
  category: string;
  description: string | null;
}

export interface PromptTemplate {
  name: string;
  version: string;
  category: string;
  description: string | null;
  model: string | null;
  temperature: number;
  maxTokens: number | null;
  variables: string[];
  systemPrompt: string | null;
  userPrompt: string;
}
