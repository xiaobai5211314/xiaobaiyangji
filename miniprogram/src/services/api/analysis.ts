import { get } from '../request';

export interface DailyReport {
  date?: string;
  fundCount?: number;
  totalAssets?: number;
  dailyProfit?: number;
  dailyRate?: number;
  totalProfit?: number;
  topContribution?: AnalysisFundRow;
  biggestDrag?: AnalysisFundRow;
  mainTheme?: ThemeExposure;
  suggestion?: string;
  [key: string]: unknown;
}

export interface AnalysisFundRow {
  code?: string;
  name?: string;
  assets?: number;
  cost?: number;
  dailyProfit?: number;
  dailyRate?: number;
  totalProfit?: number;
  totalRate?: number;
  recoveryRate?: number;
  confidence?: {
    score?: number;
    level?: string;
    reasons?: string[];
  };
  [key: string]: unknown;
}

export interface ThemeExposure {
  theme?: string;
  fundCount?: number;
  assets?: number;
  weight?: number;
  dailyProfit?: number;
  funds?: AnalysisFundRow[];
}

export interface InsightsDashboard {
  success?: boolean;
  username?: string;
  date?: string;
  generatedAt?: string;
  dailyReport?: DailyReport;
  exposure?: {
    totalAssets?: number;
    top3Concentration?: number;
    riskLevel?: string;
    themes?: ThemeExposure[];
  };
  recovery?: {
    lossCount?: number;
    averageRecoveryRate?: number;
    items?: AnalysisFundRow[];
  };
  confidence?: {
    averageScore?: number;
    lowConfidenceCount?: number;
    items?: AnalysisFundRow[];
  };
  [key: string]: unknown;
}

export interface ArchiveRow {
  fundCode?: string;
  fundName?: string;
  recordDate?: string;
  assets?: number;
  dailyProfit?: number;
  dailyRate?: number;
  totalProfit?: number;
  totalRate?: number;
  [key: string]: unknown;
}

export function getInsightsDashboard(username: string) {
  return get<InsightsDashboard>(`/api/fund/insights/dashboard?username=${encodeURIComponent(username)}`, {
    loadingText: '读取盈亏',
    fallbackData: {}
  });
}

export function getArchives(username: string, limit = 120) {
  return get<ArchiveRow[]>(`/api/fund/get-archives?username=${encodeURIComponent(username)}&limit=${limit}`, {
    loadingText: '读取档案',
    fallbackData: []
  });
}
