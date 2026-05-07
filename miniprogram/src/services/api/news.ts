import { get } from '../request';

export interface NewsItem {
  id?: string;
  title?: string;
  summary?: string;
  showTime?: string;
  timeText?: string;
  dateText?: string;
  source?: string;
  url?: string;
  important?: boolean;
  sentiment?: string;
  impactScore?: number;
  matchedFundCode?: string;
  matchedFundName?: string;
  tags?: string[];
  sort?: number;
  [key: string]: unknown;
}

export interface NewsResponse {
  mode?: string;
  source?: string;
  updatedAt?: string;
  count?: number;
  items?: NewsItem[];
}

export function getGlobalNews(force = false, important = false, limit = 60) {
  const query = `mode=global&important=${important}&limit=${limit}${force ? '&force=true' : ''}`;
  return get<NewsResponse>(`/api/fund/news?${query}`, {
    loadingText: '读取资讯'
  });
}

export function getHoldingNews(username: string, force = false, important = false, limit = 40) {
  const query = `username=${encodeURIComponent(username)}&important=${important}&limit=${limit}${force ? '&force=true' : ''}`;
  return get<NewsResponse>(`/api/fund/holding-news?${query}`, {
    loadingText: '读取持仓资讯'
  });
}
