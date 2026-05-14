import { getApiBaseUrl } from '../config';
import { get, postJson, request } from '../request';

export type StockKlinePeriod = 'minute' | 'hour' | 'day' | 'month' | 'year';

export interface StockBaseItem {
  id?: number;
  code?: string;
  stockCode?: string;
  symbol?: string;
  market?: string;
  exchange?: string;
  type?: string;
  name?: string;
  stockName?: string;
  securityName?: string;
  price?: number;
  current?: number;
  latest?: number;
  last?: number;
  close?: number;
  changeAmount?: number;
  changeRate?: number;
  rate?: number;
  pct?: number;
  percent?: number;
  changePercent?: number;
  [key: string]: unknown;
}

export interface StockHoldingItem extends StockBaseItem {
  shares?: number;
  amount?: number;
  quantity?: number;
  count?: number;
  holdAmount?: number;
  costPrice?: number;
  costAmount?: number;
  marketValue?: number;
  value?: number;
  totalValue?: number;
  totalProfit?: number;
  totalProfitRate?: number;
  profit?: number;
  holdingProfit?: number;
  income?: number;
  gain?: number;
}

export interface StockSearchItem extends StockBaseItem {}
export interface StockWatchItem extends StockBaseItem {}

export interface StockDashboardResponse {
  success?: boolean;
  message?: string;
  username?: string;
  holdings?: StockHoldingItem[];
  watchList?: StockWatchItem[];
  updatedAt?: string;
  [key: string]: unknown;
}

export interface StockSearchResponse {
  success?: boolean;
  message?: string;
  items?: StockSearchItem[];
  [key: string]: unknown;
}

export interface StockSaveHoldingRequest {
  username: string;
  stockCode: string;
  stockName?: string;
  shares: number;
  costPrice: number;
  costAmount: number;
  market?: string;
}

export interface StockSaveWatchRequest {
  username: string;
  stockCode: string;
  stockName?: string;
  market?: string;
}

export interface StockMutationResponse {
  success?: boolean;
  message?: string;
  item?: StockBaseItem | null;
  [key: string]: unknown;
}

export interface StockKlineItem {
  time?: string;
  date?: string;
  datetime?: string;
  tradeTime?: string;
  open?: number;
  close?: number;
  high?: number;
  highest?: number;
  low?: number;
  lowest?: number;
  volume?: number;
  amount?: number;
  changeRate?: number;
  [key: string]: unknown;
}

export interface StockKlinesResponse {
  success?: boolean;
  message?: string;
  code?: string;
  period?: StockKlinePeriod;
  items?: StockKlineItem[];
  [key: string]: unknown;
}

export interface StockOcrPreviewItem {
  id?: number;
  stockCode?: string;
  stockName?: string;
  recognizedName?: string;
  market?: string;
  action?: 'holding' | 'watch' | string;
  shares?: number | null;
  costPrice?: number | null;
  costAmount?: number | null;
  marketValue?: number | null;
  floatingProfit?: number | null;
  floatingProfitRate?: number | null;
  note?: string;
  [key: string]: unknown;
}

export interface StockOcrPreviewResponse {
  success?: boolean;
  message?: string;
  batchId?: number;
  count?: number;
  items?: StockOcrPreviewItem[];
  diagnostics?: string[];
  [key: string]: unknown;
}

export interface StockOcrConfirmRequest {
  username: string;
  batchId: number;
  items: StockOcrPreviewItem[];
}

export interface StockOcrConfirmResponse {
  success?: boolean;
  message?: string;
  saved?: number;
  skipped?: unknown[];
  [key: string]: unknown;
}

export function getStockDashboard(username: string) {
  return get<StockDashboardResponse>(`/api/stock/dashboard?username=${encodeURIComponent(username)}`, {
    loadingText: '读取股票',
    fallbackData: { holdings: [], watchList: [] }
  });
}

export function searchStocks(keyword: string) {
  return get<StockSearchResponse>(`/api/stock/search?keyword=${encodeURIComponent(keyword)}`, {
    loadingText: '查询股票',
    fallbackData: { success: false, message: '查询失败，请稍后重试', items: [] }
  });
}

export function saveStockWatch(payload: StockSaveWatchRequest) {
  return postJson<StockMutationResponse, StockSaveWatchRequest>('/api/stock/watch', payload, {
    loadingText: '保存自选'
  });
}

export function deleteStockWatch(username: string, code: string, market?: string) {
  const query = buildIdentityQuery(username, code, market);
  return request<StockMutationResponse>(`/api/stock/watch?${query}`, {
    method: 'DELETE',
    loadingText: '移除自选'
  });
}

export function saveStockHolding(payload: StockSaveHoldingRequest) {
  return postJson<StockMutationResponse, StockSaveHoldingRequest>('/api/stock/holding', payload, {
    loadingText: '保存持仓'
  });
}

export function deleteStockHolding(username: string, code: string, market?: string) {
  const query = buildIdentityQuery(username, code, market);
  return request<StockMutationResponse>(`/api/stock/holding?${query}`, {
    method: 'DELETE',
    loadingText: '删除持仓'
  });
}

export function getStockKlines(code: string, period: StockKlinePeriod) {
  const query = `code=${encodeURIComponent(code)}&period=${encodeURIComponent(period)}`;
  return get<StockKlinesResponse>(`/api/stock/klines?${query}`, {
    loadingText: '读取走势',
    fallbackData: { items: [] }
  });
}

export function previewStockOcr(username: string, filePath: string) {
  const url = `${getApiBaseUrl()}/api/stock/import-ocr-preview`;

  return new Promise<StockOcrPreviewResponse>((resolve, reject) => {
    uni.uploadFile({
      url,
      filePath,
      name: 'image',
      formData: {
        username
      },
      success: (result) => {
        const statusCode = Number(result.statusCode || 0);
        if (statusCode < 200 || statusCode >= 300) {
          reject(new Error(extractUploadMessage(result.data, `股票 OCR 预览失败：${statusCode}`)));
          return;
        }

        resolve(parseUploadData(result.data));
      },
      fail: reject
    });
  });
}

export function confirmStockOcr(payload: StockOcrConfirmRequest) {
  return postJson<StockOcrConfirmResponse, StockOcrConfirmRequest>('/api/stock/import-ocr-confirm', payload, {
    loadingText: '确认导入'
  });
}

function buildIdentityQuery(username: string, code: string, market?: string) {
  const query = [
    `username=${encodeURIComponent(username)}`,
    `code=${encodeURIComponent(code)}`
  ];
  if (market) query.push(`market=${encodeURIComponent(market)}`);
  return query.join('&');
}

function parseUploadData(data: unknown): StockOcrPreviewResponse {
  if (typeof data !== 'string') return (data || {}) as StockOcrPreviewResponse;
  const text = data.trim();
  if (!text) return {};

  try {
    return JSON.parse(text) as StockOcrPreviewResponse;
  } catch {
    return { success: false, message: text };
  }
}

function extractUploadMessage(data: unknown, fallback: string) {
  const parsed = parseUploadData(data);
  const body = parsed as Record<string, unknown>;
  const value = parsed.message || body.msg || body.title || body.error;
  return value ? String(value) : fallback;
}
