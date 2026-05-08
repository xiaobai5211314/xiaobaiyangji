import { get } from '../request';

export interface SectorSummary {
  key?: string;
  name?: string;
  rate?: number;
  fundCount?: number;
  streakDays?: number;
  holdingRank?: number;
  updatedAt?: string;
  previewFunds?: Array<{
    code?: string;
    name?: string;
    rate?: number;
    updatedAt?: string;
  }>;
  [key: string]: unknown;
}

export interface SectorRadarResponse {
  source?: string;
  updatedAt?: string;
  top?: SectorSummary[];
  bottom?: SectorSummary[];
  all?: SectorSummary[];
}

export interface CapitalFlowRow {
  code?: string;
  name?: string;
  rate?: number;
  mainNet?: number;
  mainNetText?: string;
  mainRatio?: number;
  superNet?: number;
  bigNet?: number;
  [key: string]: unknown;
}

export interface CapitalFlowResponse {
  source?: string;
  updatedAt?: string;
  rows?: CapitalFlowRow[];
  inflow?: CapitalFlowRow[];
  outflow?: CapitalFlowRow[];
}

const CAPITAL_FLOW_EXCLUDED_NAME_KEYWORDS = [
  '概念',
  '新高',
  '近期新高',
  '百日新高',
  '历史新高',
  '昨日涨停',
  '昨日连板',
  '连板',
  '融资',
  '融券',
  '沪股通',
  '深股通',
  '陆股通',
  '北向资金',
  '南向资金',
  '预盈',
  '预增',
  '预亏',
  '预减',
  '次新',
  '高送转',
  '重仓',
  'QFII',
  'OFII',
  '社保',
  '养老金',
  'MSCI',
  '富时罗素',
  '中特估',
  'ST',
  '小米',
  '华为',
  '苹果',
  '周期股',
  '最近多板',
  '反内卷',
  '深圳特区',
  '上海自贸',
  '海南自贸',
  '西部大开发',
  '雄安新区',
  '粤港澳',
  '长三角',
  '京津冀',
  '一带一路',
  '乡村振兴',
  '3D打印',
  'AH股',
  '氢能源',
  '特高压',
  '数字芯片设计',
  '机器人',
  '人形机器人',
  '飞行汽车',
  '低空经济',
  '数据要素',
  '算力',
  'AIGC',
  'ChatGPT',
  '虚拟现实',
  '元宇宙',
  '东数西算',
  '国企改革',
  '央企改革',
  '参股',
  '壳资源',
  '股权转让',
  '摘帽',
  '低价股',
  '高股息',
  '破净'
];

export interface GlobalIndexKline {
  date?: string;
  rate?: number;
  close?: number;
  point?: number;
  latest?: number;
  value?: number;
  indexValue?: number;
  current?: number;
  price?: number;
  changePercent?: number;
  pct?: number;
  [key: string]: unknown;
}

export interface GlobalIndexItem {
  code?: string;
  name?: string;
  secid?: string;
  market?: 'cn' | 'hk' | 'us' | 'other';
  latest?: number | null;
  todayRate?: number | null;
  yearRate?: number | null;
  updatedAt?: string;
  klines?: GlobalIndexKline[];
  [key: string]: unknown;
}

export function getSectors(force = false) {
  return get<SectorRadarResponse>(`/api/fund/sectors${force ? '?force=true' : ''}`, {
    loadingText: '读取板块'
  });
}

export function getCapitalFlow(force = false, limit = 30) {
  const query = `limit=${limit}${force ? '&force=true' : ''}`;
  return get<CapitalFlowResponse>(`/api/fund/capital-flow?${query}`, {
    loadingText: '读取资金'
  }).then(normalizeCapitalFlowResponse);
}

export function getGlobalIndices() {
  return get<unknown>('/api/fund/global-indices', {
    loadingText: '读取大盘'
  }).then((payload) => {
    console.log('[global-indices raw response]', payload);
    const rows = extractGlobalIndexRows(payload).map(normalizeGlobalIndexRow);
    rows.forEach((item) => {
      console.log('[global-indices normalized]', {
        name: item.name,
        point: item.point ?? item.latest ?? null,
        todayRate: item.todayRate ?? null,
        yearRate: item.yearRate ?? null
      });
    });
    return rows;
  });
}

function normalizeIndexName(value: unknown) {
  return String(value || '').replace(/\s*\((?:无数据|异常:.*)\)\s*$/g, '').trim();
}

function extractGlobalIndexRows(payload: unknown): GlobalIndexItem[] {
  if (Array.isArray(payload)) return payload as GlobalIndexItem[];
  if (!payload || typeof payload !== 'object') return [];

  const source = payload as Record<string, unknown>;
  const rows = firstArray(source.rows, source.items, source.list, source.data, source.indices);
  return rows as GlobalIndexItem[];
}

function normalizeGlobalIndexRow(row: GlobalIndexItem): GlobalIndexItem {
  const source = row as Record<string, unknown>;
  const name = normalizeIndexName(source.name || source.indexName || source.title || source.shortName);
  const code = source.code || source.indexCode || source.symbol || source.secid;
  const market = normalizeMarket(source.market || source.type || source.category, name, code);
  const history = firstArray(source.klines, source.lines, source.history, source.series, source.data, source.items, source.list) as GlobalIndexKline[];
  const point = firstNumber(
    source.point,
    source.latest,
    source.close,
    source.value,
    source.indexValue,
    source.current,
    source.price
  );

  return {
    ...row,
    name: name || row.name,
    code: code === undefined || code === null ? row.code : String(code),
    market,
    latest: point,
    point,
    todayRate: firstNumber(source.todayRate, source.rate, source.changePercent, source.pct, source.pctChg),
    yearRate: firstNumber(source.yearRate, source.oneYearRate, source.annualRate, source.yearChangePercent),
    klines: history
  };
}

function normalizeMarket(value: unknown, name: unknown, code: unknown): 'cn' | 'hk' | 'us' | 'other' {
  const marketText = String(value || '').toUpperCase();
  if (/港|HK|HONG/.test(marketText)) return 'hk';
  if (/美|US|USA|NASDAQ|NYSE/.test(marketText)) return 'us';
  if (/A股|沪|深|CN|CHINA|大陆/.test(marketText)) return 'cn';

  const text = `${name || ''} ${code || ''}`.toUpperCase();
  if (/恒生|HSI|HSTECH|港股|香港/.test(text)) return 'hk';
  if (/纳斯达克|标普|道琼斯|NDX|IXIC|SPX|DJIA|NASDAQ|S&P/.test(text)) return 'us';
  if (/上证|科创|创业板|沪深|中证|000001|000688|399006/.test(text)) return 'cn';
  return 'other';
}

function firstNumber(...values: unknown[]) {
  for (const value of values) {
    if (value === null || value === undefined) continue;
    if (typeof value === 'string' && value.trim() === '') continue;
    const n = Number(value);
    if (Number.isFinite(n)) return n;
  }

  return null;
}

function firstArray(...values: unknown[]) {
  for (const value of values) {
    if (Array.isArray(value)) return value;
  }

  return [];
}

export function isPureIndustryCapitalFlowItem(item: CapitalFlowRow) {
  const source = item as Record<string, unknown>;
  const name = String(source.name || source.Name || source.title || source.Title || '').trim();
  if (!name) return false;

  const upperName = name.toUpperCase();
  return !CAPITAL_FLOW_EXCLUDED_NAME_KEYWORDS.some((keyword) => {
    const upperKeyword = keyword.toUpperCase();
    return upperName.includes(upperKeyword);
  });
}

export function filterIndustryCapitalFlowRows(rows: CapitalFlowRow[] = []) {
  return rows.filter(isPureIndustryCapitalFlowItem);
}

function normalizeCapitalFlowResponse(payload: CapitalFlowResponse) {
  const rows = filterIndustryCapitalFlowRows(payload.rows || []);
  const inflow = filterIndustryCapitalFlowRows(payload.inflow?.length ? payload.inflow : rows);
  const outflow = filterIndustryCapitalFlowRows(payload.outflow?.length ? payload.outflow : rows);

  return {
    ...payload,
    rows,
    inflow,
    outflow
  };
}
