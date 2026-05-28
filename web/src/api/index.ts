import axios from 'axios';

const api = axios.create({
  baseURL: 'http://localhost:5172/api',
  timeout: 30000,
});

export const getHealth = () => api.get('/health');

export const getPositions = () => api.get('/execution/positions');
export const getPosition = (code: string) => api.get(`/execution/positions/${code}`);
export const placeOrder = (order: any) => api.post('/execution/order', order);
export const getOrderStatus = (orderId: string) => api.get(`/execution/order/${orderId}/status`);

export const getMarketState = () => api.get('/feature/market/state');
export const getMarketSentiment = () => api.get('/feature/market/sentiment');
export const getIndicators = (code: string) => api.get(`/feature/${code}/indicators`);

export const runBacktest = (config: any) => api.post('/strategy/backtest', config);

export const checkRisk = (data: any) => api.post('/risk/check', data);

export const getAgents = () => api.get('/orchestrator/agents');
export const analyzeStock = (code: string) => api.post('/orchestrator/analyze', { code });
export const generateSignal = (code: string) => api.post('/orchestrator/signal', { code });
export const makeDecision = (code: string, totalCapital: number) => 
  api.post('/orchestrator/decision', { code, totalCapital });

export default api;
