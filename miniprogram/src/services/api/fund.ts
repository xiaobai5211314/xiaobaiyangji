import { getApiBaseUrl } from '../config';
import { get, postJson } from '../request';

export interface FundTodayItem {
  code: string;
  name: string;
  amount: number;
  rawHoldAmount?: number;
  confirmedAmount?: number;
  pendingBuy?: boolean;
  pendingBuyAmount?: number;
  pendingTradeDate?: string;
  pendingTradeTime?: string;
  pendingTradeStatus?: string;
  pendingConfirmDate?: string;
  pendingSource?: string;
  pendingNote?: string;
  shares?: number;
  cost?: number | null;
  rawCostAmount?: number | null;
  confirmedCost?: number | null;
  realizedProfit?: number;
  holdingIncome?: number;
  holdingRate?: number;
  holdingSource?: string;
  existingReturnRate?: number;
  breakEvenRate?: number;
  reliabilityScore?: number;
  reliabilityLevel?: string;
  currentRate?: number;
  rawCurrentRate?: number;
  diffRate?: number;
  isSettled?: boolean;
  isHoliday?: boolean;
  actualRate?: number;
  actualExactProfit?: number;
  actualExactProfitSource?: string;
  lastTradeDate?: string;
  lastAddAmount?: number;
  lastSettledDate?: string;
  lastSettledProfit?: number;
  lastSettledRate?: number;
  todayBaseAmount?: number;
  todayProfitPreview?: number;
  todayRateForSimulation?: number;
  profitSource?: string;
  marketOpen?: boolean;
  marketStatus?: string;
  marketLabel?: string;
  effectiveDate?: string;
  calibrationOffset?: number;
  calibrationSamples?: number;
  calibrationLastError?: number;
  calibrationConfidence?: string;
  calibrationNote?: string;
  todayProfit?: number;
  totalProfit?: number;
  estimatedProfit?: number;
  todayAmount?: number;
  todayNav?: number | string | null;
  nav?: number | string | null;
  netValue?: number | string | null;
  latestNav?: number | string | null;
  latestNetValue?: number | string | null;
  unitNetValue?: number | string | null;
  dwjz?: number | string | null;
  actualNav?: number | string | null;
  estimate?: number | string | null;
  valuation?: number | string | null;
  estimatedNav?: number | string | null;
  estimateNav?: number | string | null;
  valuationValue?: number | string | null;
  currentEstimate?: number | string | null;
  gsz?: number | string | null;
  estimateRate?: number | string | null;
  valuationRate?: number | string | null;
  gszzl?: number | string | null;
  estimatedRate?: number | string | null;
  estimateDeviation?: number | string | null;
  valuationDeviation?: number | string | null;
  navDeviation?: number | string | null;
  premiumRate?: number | string | null;
  data?: Array<[unknown, unknown]>;
  breakEvenSimulator?: unknown[];
  [key: string]: unknown;
}

export interface OcrImportPreviewItem {
  ocrName?: string;
  code?: string;
  name?: string;
  matchScore?: number;
  holdAmount?: number;
  costAmount?: number;
  holdingIncome?: number;
  yesterdayIncome?: number;
  holdingRate?: number;
  holdShares?: number;
  calcMethod?: string;
  warning?: string;
  isPendingBuy?: boolean;
  isSuspiciousPendingBuy?: boolean;
  pendingBuyAmount?: number;
  pendingReason?: string;
  pendingConfirmDate?: string;
  pendingSource?: string;
  confirmedAmount?: number;
  todayBaseAmount?: number;
  participatesToday?: boolean;
  [key: string]: unknown;
}

export interface OcrImportPreviewResponse {
  success?: boolean;
  count?: number;
  items?: OcrImportPreviewItem[];
  diagnostics?: string[];
  message?: string;
  [key: string]: unknown;
}

export interface OcrImportConfirmRequest {
  username: string;
  items: OcrImportPreviewItem[];
}

export interface OcrImportConfirmResponse {
  success?: boolean;
  imported?: number;
  message?: string;
  [key: string]: unknown;
}

export interface FundArchiveRow {
  fundCode?: string;
  fundName?: string;
  recordDate?: string;
  assets?: number;
  cost?: number;
  dailyProfit?: number;
  dailyRate?: number;
  totalProfit?: number;
  totalRate?: number;
  [key: string]: unknown;
}

export function getTodayFunds(username: string, force = false, silent = false) {
  const query = `username=${encodeURIComponent(username)}${force ? '&force=true' : ''}`;
  return get<unknown>(`/api/fund/today?${query}`, {
    loadingText: '读取持仓',
    silent,
    fallbackData: []
  }).then((raw) => {
    if (Array.isArray(raw)) return raw as FundTodayItem[];
    if (raw && typeof raw === 'object' && 'funds' in raw) return (raw as { funds: FundTodayItem[] }).funds;
    return [] as FundTodayItem[];
  });
}

export function getFundArchives(username: string, fundCode: string, limit = 365) {
  const query = `username=${encodeURIComponent(username)}&fundCode=${encodeURIComponent(fundCode)}&limit=${limit}`;
  return get<FundArchiveRow[]>(`/api/fund/get-archives?${query}`, {
    loadingText: '读取历史',
    fallbackData: []
  });
}

function parseUploadData(data: unknown): OcrImportPreviewResponse {
  if (typeof data !== 'string') return (data || {}) as OcrImportPreviewResponse;
  const text = data.trim();
  if (!text) return {};

  try {
    return JSON.parse(text) as OcrImportPreviewResponse;
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

export function previewFundOcr(username: string, filePath: string) {
  const url = `${getApiBaseUrl()}/api/Fund/import-ocr-preview?username=${encodeURIComponent(username)}`;

  return new Promise<OcrImportPreviewResponse>((resolve, reject) => {
    uni.uploadFile({
      url,
      filePath,
      name: 'imageFile',
      success: (result) => {
        const statusCode = Number(result.statusCode || 0);
        if (statusCode < 200 || statusCode >= 300) {
          reject(new Error(extractUploadMessage(result.data, `OCR 预览失败：${statusCode}`)));
          return;
        }

        resolve(parseUploadData(result.data));
      },
      fail: (error) => {
        reject(error);
      }
    });
  });
}

export function confirmFundOcr(payload: OcrImportConfirmRequest) {
  return postJson<OcrImportConfirmResponse, OcrImportConfirmRequest>('/api/Fund/import-ocr-confirm', payload, {
    loadingText: '确认导入'
  });
}

export interface FundNavHistoryItem {
  date?: string;
  nav?: string | number;
  rate?: string | number;
  [key: string]: unknown;
}

export function getFundNavHistory(code: string, period = '1y') {
  return get<FundNavHistoryItem[]>(`/api/fund/nav-history?code=${encodeURIComponent(code)}&period=${period}`, {
    loadingText: '读取走势',
    silent: true,
    fallbackData: []
  });
}
