<template>
  <view :class="['page-shell', 'sector-page', themeClass]">
    <view class="page-header">
      <view>
        <text class="page-title">板块雷达</text>
        <text class="page-subtitle">{{ updatedAtText }}</text>
      </view>
      <text class="chip">{{ sectorCount }} 个主题</text>
    </view>

    <view v-if="isStaleData && !loading" class="stale-indicator">
      <text class="stale-text">使用缓存 · 后台更新中</text>
    </view>

    <view class="glass-card notice-card">
      <view class="notice-text">数据仅供个人记录与行情参考，不构成投资建议，实际数据以基金公司、交易所或券商披露为准。</view>
    </view>

    <view class="glass-card hero-card">
      <text class="muted-text">今日领涨</text>
      <view class="hero-row">
        <view>
          <text class="hero-title">{{ topList[0]?.name || '暂无板块数据' }}</text>
          <text class="hero-subtitle">{{ sectorPayload.source || '主题基金池 · 估值/净值参考' }}</text>
        </view>
        <text :class="['hero-rate', 'finance-number', optionalProfitClass(topList[0]?.rate)]">
          {{ signedOptionalPercent(topList[0]?.rate) }}
        </text>
      </view>
    </view>

    <view class="board-grid">
      <view class="glass-card board-card">
        <view class="board-head">
          <text class="section-title compact-title">涨幅榜</text>
          <text class="muted-text">Top {{ topList.length }}</text>
        </view>
        <view v-if="topList.length === 0" class="empty-mini">暂无涨幅数据</view>
        <view v-for="(item, index) in topList.slice(0, 8)" :key="sectorKey(item, index, 'top')" class="board-row" @tap="openSectorDetail(item)">
          <text class="rank-badge">{{ index + 1 }}</text>
          <view class="row-main">
            <text class="row-title">{{ item.name || '未知板块' }}</text>
            <text class="row-sub">指数 {{ item.indexFundCount || 0 }} · 主动 {{ item.activeFundCount || 0 }}</text>
          </view>
          <text :class="['row-rate', 'finance-number', optionalProfitClass(item.rate)]">{{ signedOptionalPercent(item.rate) }}</text>
        </view>
      </view>

      <view class="glass-card board-card">
        <view class="board-head">
          <text class="section-title compact-title">跌幅榜</text>
          <text class="muted-text">Bottom {{ bottomList.length }}</text>
        </view>
        <view v-if="bottomList.length === 0" class="empty-mini">暂无跌幅数据</view>
        <view v-for="(item, index) in bottomList.slice(0, 8)" :key="sectorKey(item, index, 'bottom')" class="board-row" @tap="openSectorDetail(item)">
          <text class="rank-badge loss-badge">{{ index + 1 }}</text>
          <view class="row-main">
            <text class="row-title">{{ item.name || '未知板块' }}</text>
            <text class="row-sub">指数 {{ item.indexFundCount || 0 }} · 主动 {{ item.activeFundCount || 0 }}</text>
          </view>
          <text :class="['row-rate', 'finance-number', optionalProfitClass(item.rate)]">{{ signedOptionalPercent(item.rate) }}</text>
        </view>
      </view>
    </view>

    <view class="list-head catalog-head">
      <view>
        <text class="section-title">全部主题基金</text>
        <text class="list-subtitle">覆盖指数与主动混合/股票基金，点击查看明细</text>
      </view>
      <text class="muted-text">{{ catalogList.length }} 个</text>
    </view>
    <view class="glass-card catalog-card">
      <input v-model="sectorSearch" class="catalog-search" type="text" placeholder="搜索航天、卫星、低空经济等主题" />
      <view v-if="catalogList.length === 0" class="empty-mini">没有匹配主题</view>
      <view v-for="(item, index) in catalogList" :key="sectorKey(item, index, 'catalog')" class="catalog-row" @tap="openSectorDetail(item)">
        <view class="row-main">
          <text class="row-title">{{ item.name || '未知主题' }}</text>
          <text class="row-sub">共 {{ sectorFundCount(item) }} 只 · 指数 {{ item.indexFundCount || 0 }} · 主动 {{ item.activeFundCount || 0 }}</text>
        </view>
        <text :class="['row-rate', 'finance-number', optionalProfitClass(item.rate)]">{{ signedOptionalPercent(item.rate) }}</text>
      </view>
    </view>

    <view class="list-head capital-heading">
      <view>
        <text class="section-title">股票行业主力资金</text>
        <text class="list-subtitle">股票行业口径，不是场外基金申购赎回</text>
      </view>
    </view>

    <view class="board-grid">
      <view class="glass-card board-card">
        <view class="board-head">
          <text class="section-title compact-title">行业净流入</text>
          <text class="muted-text">{{ flowPayload.updatedAt || '' }}</text>
        </view>
        <view v-if="inflowList.length === 0" class="empty-mini">暂无流入数据</view>
        <view v-for="(item, index) in inflowList.slice(0, 8)" :key="flowKey(item, index, 'in')" class="board-row">
          <text class="rank-badge">{{ index + 1 }}</text>
          <view class="row-main">
            <text class="row-title">{{ item.name || '未知行业' }}</text>
            <text class="row-sub">主力占比 {{ signedOptionalPercent(item.mainRatio) }}</text>
          </view>
          <view class="money-column">
            <text :class="['finance-number', optionalProfitClass(item.mainNet)]">{{ item.mainNetText || signedOptionalMoney(item.mainNet) }}</text>
            <text :class="['small-rate', optionalProfitClass(item.rate)]">{{ signedOptionalPercent(item.rate) }}</text>
          </view>
        </view>
      </view>

      <view class="glass-card board-card">
        <view class="board-head">
          <text class="section-title compact-title">行业净流出</text>
          <text class="muted-text">{{ flowPayload.source || '' }}</text>
        </view>
        <view v-if="outflowList.length === 0" class="empty-mini">暂无流出数据</view>
        <view v-for="(item, index) in outflowList.slice(0, 8)" :key="flowKey(item, index, 'out')" class="board-row">
          <text class="rank-badge loss-badge">{{ index + 1 }}</text>
          <view class="row-main">
            <text class="row-title">{{ item.name || '未知行业' }}</text>
            <text class="row-sub">主力占比 {{ signedOptionalPercent(item.mainRatio) }}</text>
          </view>
          <view class="money-column">
            <text :class="['finance-number', optionalProfitClass(item.mainNet)]">{{ item.mainNetText || signedOptionalMoney(item.mainNet) }}</text>
            <text :class="['small-rate', optionalProfitClass(item.rate)]">{{ signedOptionalPercent(item.rate) }}</text>
          </view>
        </view>
      </view>
    </view>

    <view class="list-head">
      <view>
        <text class="section-title">大盘指数</text>
      <text class="list-subtitle">点击查看近 1 年走势</text>
      </view>
      <text class="muted-text">下拉刷新</text>
    </view>

    <view v-if="visibleIndices.length === 0 && !loading" class="glass-card empty-card" @tap="loadData(true)">
      <text>暂无大盘指数数据，点击重试或下拉刷新</text>
    </view>

    <view v-for="group in indexGroups" :key="group.key" class="index-group">
      <text class="group-title">{{ group.title }}</text>
      <view v-for="(item, index) in group.items" :key="indexKey(item, index)" class="glass-card index-card" @tap="openIndexDetail(item)">
        <view class="index-head">
          <view>
            <text class="index-name">{{ displayIndexName(item) }}</text>
            <text class="index-sub">点位 {{ indexPointText(item) }}</text>
            <text v-if="item.updatedAt" class="index-sub">更新 {{ item.updatedAt }}</text>
            <text v-if="!indexHasMarketData(item)" class="index-sub warning-sub">暂无数据</text>
          </view>
          <view class="index-rates">
            <text :class="['finance-number', optionalProfitClass(indexRateValue(item.todayRate, item))]">今 {{ indexPercentText(item.todayRate, item) }}</text>
            <text :class="['small-rate', optionalProfitClass(indexYearRateValue(item))]">1年 {{ indexYearPercentText(item) }}</text>
          </view>
        </view>
        <text class="index-action">查看详情</text>
      </view>
    </view>

    <view v-if="detailOpen" class="detail-mask" @tap.self="closeSectorDetail">
      <view class="detail-sheet">
        <view class="detail-head">
          <view class="detail-title-wrap">
            <text class="detail-title">{{ detailPayload.name || selectedSector?.name || '主题基金' }}</text>
            <text class="detail-sub">{{ detailPayload.rateScope || '行情参考' }} · 共 {{ detailPayload.fundCount || 0 }} 只</text>
          </view>
          <button class="detail-close" @tap="closeSectorDetail">×</button>
        </view>
        <view class="detail-tabs">
          <button :class="['detail-tab', detailGroup === 'all' ? 'active' : '']" @tap="switchDetailGroup('all')">全部 {{ detailGroupCount('all') }}</button>
          <button :class="['detail-tab', detailGroup === 'index' ? 'active' : '']" @tap="switchDetailGroup('index')">指数 {{ detailGroupCount('index') }}</button>
          <button :class="['detail-tab', detailGroup === 'active' ? 'active' : '']" @tap="switchDetailGroup('active')">主动 {{ detailGroupCount('active') }}</button>
        </view>
        <view class="detail-search-row">
          <input v-model="detailSearch" class="detail-search" type="text" confirm-type="search" placeholder="搜索名称、代码或类型" @confirm="searchDetailFunds" />
          <button class="detail-search-btn" @tap="searchDetailFunds">搜索</button>
        </view>
        <scroll-view scroll-y class="detail-list">
          <view v-if="detailLoading && detailFunds.length === 0" class="detail-empty">正在读取主题基金...</view>
          <view v-else-if="detailFunds.length === 0" class="detail-empty">暂无匹配基金</view>
          <view v-for="fund in detailFunds" :key="fund.code" class="detail-fund-row">
            <view class="detail-fund-main">
              <text class="detail-fund-name">{{ fund.name || '未知基金' }}</text>
              <text class="detail-fund-type">{{ fund.code || '--' }} · {{ fund.fundType || '类型待核实' }}</text>
              <text :class="['detail-quote', quoteStatusClass(fund)]">{{ (fund.quoteLabel || '暂无可用行情') + (fund.updatedAt ? (' · ' + fund.updatedAt) : '') }}</text>
            </view>
            <view class="detail-rate-col">
              <text :class="['detail-rate', optionalProfitClass(fund.rate)]">{{ signedOptionalPercent(fund.rate) }}</text>
              <text :class="['detail-month', optionalProfitClass(fund.monthRate)]">近1月 {{ fund.monthRate == null ? '--' : signedOptionalPercent(fund.monthRate) }}</text>
            </view>
          </view>
          <button v-if="detailPayload.hasMore" class="load-more" :disabled="detailLoading" @tap="loadSectorDetail(true)">{{ detailLoading ? '加载中...' : '加载更多' }}</button>
        </scroll-view>
      </view>
    </view>

    <view class="safe-tabbar-space" />
    <AppTabBar active="sector" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import AppTabBar from '../../components/AppTabBar.vue';
import {
  filterIndustryCapitalFlowRows,
  getCapitalFlow,
  getGlobalIndices,
  getSectorFunds,
  getSectors,
  type CapitalFlowResponse,
  type CapitalFlowRow,
  type GlobalIndexItem,
  type SectorFundGroup,
  type SectorFundQuote,
  type SectorFundResponse,
  type SectorRadarResponse,
  type SectorSummary
} from '../../services/api/sector';
import { optionalProfitClass, signedMoney, signedPercent } from '../../utils/format';
import { loadTheme, themeClass } from '../../stores/theme';
import { getLocalStorageCache, setLocalStorageCache } from '../../services/request';

const loading = ref(false);
const sectorPayload = ref<SectorRadarResponse>({});
const flowPayload = ref<CapitalFlowResponse>({});
const indices = ref<GlobalIndexItem[]>([]);
const PAGE_CACHE_TTL = 60000;
const loadedAt = ref(0);
const isStaleData = ref(false);
const staleUpdatedAt = ref('');
const sectorSearch = ref('');
const detailOpen = ref(false);
const detailLoading = ref(false);
const selectedSector = ref<SectorSummary | null>(null);
const detailGroup = ref<SectorFundGroup>('all');
const detailSearch = ref('');
const detailFunds = ref<SectorFundQuote[]>([]);
const detailPayload = ref<SectorFundResponse>({ funds: [], groupCounts: { all: 0, index: 0, active: 0 } });
const DEBUG_FIELD_AUDIT =
  (import.meta as ImportMeta & { env?: { VITE_DEBUG_MARKET_INDEX?: string } }).env?.VITE_DEBUG_MARKET_INDEX === 'true';

const allSectors = computed(() => sectorPayload.value.all || []);
const topList = computed(() => {
  const source = sectorPayload.value.top?.length ? sectorPayload.value.top : allSectors.value;
  return [...source].sort((a, b) => Number(b.rate || 0) - Number(a.rate || 0));
});
const bottomList = computed(() => {
  const source = sectorPayload.value.bottom?.length ? sectorPayload.value.bottom : allSectors.value;
  return [...source].sort((a, b) => Number(a.rate || 0) - Number(b.rate || 0));
});
const catalogList = computed(() => {
  const keyword = sectorSearch.value.trim().toLowerCase();
  const source = [...allSectors.value].sort((a, b) => String(a.name || '').localeCompare(String(b.name || ''), 'zh-CN'));
  if (!keyword) return source;
  return source.filter((item) => `${item.name || ''} ${item.key || ''}`.toLowerCase().includes(keyword));
});
const flowRows = computed(() => flowPayload.value.rows || []);
const inflowList = computed(() => {
  const source = flowPayload.value.inflow?.length ? flowPayload.value.inflow : flowRows.value;
  return filterIndustryCapitalFlowRows(source).sort((a, b) => Number(b.mainNet || 0) - Number(a.mainNet || 0));
});
const outflowList = computed(() => {
  const source = flowPayload.value.outflow?.length ? flowPayload.value.outflow : flowRows.value;
  return filterIndustryCapitalFlowRows(source).sort((a, b) => Number(a.mainNet || 0) - Number(b.mainNet || 0));
});
const sectorCount = computed(() => allSectors.value.length || topList.value.length + bottomList.value.length);
const updatedAtText = computed(() => sectorPayload.value.updatedAt || '板块与资金流同步观察');
const visibleIndices = computed(() => indices.value.filter(hasIndexEntry));
const indexGroups = computed(() => {
  const groups = [
    { key: 'cn', title: 'A股指数', items: [] as GlobalIndexItem[] },
    { key: 'hk', title: '港股指数', items: [] as GlobalIndexItem[] },
    { key: 'us', title: '美股指数', items: [] as GlobalIndexItem[] },
    { key: 'other', title: '其他指数', items: [] as GlobalIndexItem[] }
  ];

  for (const item of visibleIndices.value) {
    const type = indexType(item);
    const group = groups.find((row) => row.key === type) || groups[0];
    group.items.push(item);
  }

  return groups.filter((group) => group.items.length > 0);
});

onShow(() => {
  loadTheme();
  loadData(false).catch((error) => console.warn('[sector:load]', error));
});

onPullDownRefresh(async () => {
  try {
    await loadData(true);
  } catch (error) {
    console.warn('[sector:pull-down-refresh]', error);
    uni.showToast({ title: '刷新失败，请稍后重试', icon: 'none' });
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadData(force: boolean) {
  if (loading.value) return;
  const hasPageData = allSectors.value.length > 0 || flowRows.value.length > 0 || indices.value.length > 0;
  if (!force && hasPageData && Date.now() - loadedAt.value < PAGE_CACHE_TTL) return;

  if (!force && !hasPageData) {
    const cachedSectors = getLocalStorageCache<SectorRadarResponse>('sector_radar_cache');
    const cachedFlow = getLocalStorageCache<CapitalFlowResponse>('capital_flow_cache');
    const cachedIndices = getLocalStorageCache<GlobalIndexItem[]>('global_indices_cache');
    if (cachedSectors || cachedFlow || cachedIndices) {
      if (cachedSectors) sectorPayload.value = cachedSectors;
      if (cachedFlow) flowPayload.value = cachedFlow;
      if (cachedIndices) indices.value = cachedIndices;
      isStaleData.value = true;
      staleUpdatedAt.value = '使用缓存';
    }
  }

  loading.value = true;
  try {
    const hasCachedData = allSectors.value.length > 0 || flowRows.value.length > 0 || indices.value.length > 0;
    const [sectorsResult, flowResult, indicesResult] = await Promise.allSettled([
      getSectors(force, hasCachedData),
      getCapitalFlow(force, 100, hasCachedData),
      getGlobalIndices(force, hasCachedData)
    ]);

    let anySuccess = false;
    if (sectorsResult.status === 'fulfilled') {
      sectorPayload.value = sectorsResult.value || {};
      setLocalStorageCache('sector_radar_cache', sectorsResult.value, 3600000);
      anySuccess = true;
    } else {
      console.warn('[sector:sectors]', sectorsResult.reason);
    }

    if (flowResult.status === 'fulfilled') {
      flowPayload.value = flowResult.value || {};
      setLocalStorageCache('capital_flow_cache', flowResult.value, 3600000);
      anySuccess = true;
    } else {
      console.warn('[sector:capital-flow]', flowResult.reason);
    }

    if (indicesResult.status === 'fulfilled') {
      indices.value = Array.isArray(indicesResult.value) ? indicesResult.value : [];
      setLocalStorageCache('global_indices_cache', indices.value, 3600000);
      anySuccess = true;
    } else {
      console.warn('[sector:global-indices]', indicesResult.reason);
    }

    if (anySuccess) {
      isStaleData.value = false;
      staleUpdatedAt.value = '';
    }
    loadedAt.value = Date.now();
    logGlobalIndicesAudit(indices.value);
  } finally {
    loading.value = false;
  }
}

function signedOptionalPercent(value: unknown) {
  if (value === null || value === undefined || value === '') return '--';
  return signedPercent(value);
}

function signedOptionalMoney(value: unknown) {
  if (value === null || value === undefined || value === '') return '--';
  return signedMoney(value);
}

function numericOrDash(value: unknown) {
  const n = Number(value);
  return Number.isFinite(n) ? n.toFixed(2) : '--';
}

function sectorKey(item: SectorSummary, index: number, prefix: string) {
  return `${prefix}-${item.key || item.name || 'sector'}-${index}`;
}

async function openSectorDetail(item: SectorSummary) {
  selectedSector.value = item;
  detailGroup.value = 'all';
  detailSearch.value = '';
  detailFunds.value = [];
  detailPayload.value = { funds: [], groupCounts: { all: 0, index: 0, active: 0 } };
  detailOpen.value = true;
  await loadSectorDetail(false);
}

async function loadSectorDetail(append: boolean) {
  if (!selectedSector.value || detailLoading.value) return;
  const key = String(selectedSector.value.key || selectedSector.value.name || '');
  if (!key) return;
  detailLoading.value = true;
  try {
    const nextPage = append ? Number(detailPayload.value.page || 1) + 1 : 1;
    const payload = await getSectorFunds(key, {
      page: nextPage,
      pageSize: 20,
      fundGroup: detailGroup.value,
      query: detailSearch.value,
      silent: true
    });
    const rows = Array.isArray(payload.funds) ? payload.funds : [];
    detailFunds.value = append ? detailFunds.value.concat(rows) : rows;
    detailPayload.value = payload;
  } catch (error) {
    console.warn('[sector:funds]', error);
    uni.showToast({ title: '主题基金加载失败', icon: 'none' });
  } finally {
    detailLoading.value = false;
  }
}

function switchDetailGroup(group: SectorFundGroup) {
  if (detailGroup.value === group) return;
  detailGroup.value = group;
  detailFunds.value = [];
  loadSectorDetail(false);
}

function searchDetailFunds() {
  detailFunds.value = [];
  loadSectorDetail(false);
}

function closeSectorDetail() {
  detailOpen.value = false;
}

function detailGroupCount(group: SectorFundGroup) {
  return Number(detailPayload.value.groupCounts?.[group] || 0);
}

function quoteStatusClass(item: SectorFundQuote) {
  if (item.quoteStatus === 'live-estimate') return 'quote-live';
  if (item.quoteStatus === 'latest-nav') return 'quote-nav';
  return 'quote-muted';
}

function flowKey(item: CapitalFlowRow, index: number, prefix: string) {
  return `${prefix}-${item.code || item.name || 'flow'}-${index}`;
}

function indexKey(item: GlobalIndexItem, index: number) {
  return `${item.name || 'index'}-${index}`;
}

function openIndexDetail(item: GlobalIndexItem) {
  const indexName = encodeURIComponent(String(item.name || ''));
  const indexCode = encodeURIComponent(String(item.code || ''));
  uni.navigateTo({ url: `/pages/index-detail/index?indexName=${indexName}&indexCode=${indexCode}` });
}

function hasIndexEntry(item: GlobalIndexItem) {
  const name = String(item.name || '');
  return Boolean(cleanIndexName(name));
}

function indexType(item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  const marketText = `${source.market || source.type || source.category || ''}`.toUpperCase();
  if (/港|HK|HONG/.test(marketText)) return 'hk';
  if (/美|US|USA|NASDAQ|NYSE/.test(marketText)) return 'us';
  if (/A股|沪|深|CN|CHINA|大陆/.test(marketText)) return 'cn';

  const text = `${item.name || ''} ${item.code || ''}`.toUpperCase();
  if (/恒生|HSI|HSTECH|港股|香港/.test(text)) return 'hk';
  if (/纳斯达克|标普|道琼斯|NDX|IXIC|SPX|DJIA|NASDAQ|S&P/.test(text)) return 'us';
  if (/上证|科创|创业板|沪深|中证|000001|000688|399006/.test(text)) return 'cn';
  return 'other';
}

function cleanIndexName(value: unknown) {
  return String(value || '').replace(/\s*\((?:无数据|异常:.*)\)\s*$/g, '').trim();
}

function displayIndexName(item: GlobalIndexItem) {
  return cleanIndexName(item.name) || '未知指数';
}

function indexHasMarketData(item: GlobalIndexItem) {
  const name = String(item.name || '');
  return (
    Boolean(cleanIndexName(name)) &&
    !/异常/.test(name) &&
    (indexPointValue(item) !== null || indexRateValue(item.todayRate, item) !== null || indexYearRateValue(item) !== null)
  );
}

function indexPointText(item: GlobalIndexItem) {
  const value = indexPointValue(item);
  return value !== null ? numericOrDash(value) : '--';
}

function indexRateValue(value: unknown, item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  const n = firstNumber(
    value,
    source.rate,
    source.changePercent,
    source.pct,
    source.pctChg
  );
  return n;
}

function sectorFundCount(item: SectorSummary) {
  const explicitCount = firstNumber(item.fundCount);
  if (explicitCount !== null) return explicitCount;

  const source = item as Record<string, unknown>;
  const legacyCount = firstNumber(source['quot' + 'edCount']);
  return legacyCount ?? 0;
}

function indexPercentText(value: unknown, item: GlobalIndexItem) {
  const n = indexRateValue(value, item);
  return n === null ? '--' : signedOptionalPercent(n);
}

function indexYearPercentText(item: GlobalIndexItem) {
  const n = indexYearRateValue(item);
  return n === null ? '--' : signedOptionalPercent(n);
}

function indexPointValue(item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  return firstPositiveNumber(
    source.point,
    source.latest,
    source.close,
    source.value,
    source.indexValue,
    source.current,
    source.price
  );
}

function indexYearRateValue(item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  return firstNumber(source.yearRate, source.oneYearRate, source.annualRate, source.yearChangePercent);
}

function indexHistoryCount(item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  const rows = firstArray(source.klines, source.lines, source.history, source.series, source.data, source.items, source.list);
  return rows.length;
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

function firstPositiveNumber(...values: unknown[]) {
  for (const value of values) {
    const n = firstNumber(value);
    if (n !== null && n > 0) return n;
  }

  return null;
}

function firstArray(...values: unknown[]) {
  for (const value of values) {
    if (Array.isArray(value)) return value;
  }

  return [];
}

function logGlobalIndicesAudit(rows: GlobalIndexItem[]) {
  if (!DEBUG_FIELD_AUDIT) return;

  rows.forEach((item) => {
    console.warn('[global.indices fields]', {
      name: item.name,
      code: item.code,
      point: indexPointValue(item),
      todayRate: indexRateValue(item.todayRate, item),
      yearRate: indexYearRateValue(item),
      historyCount: indexHistoryCount(item)
    });
    if (!indexHasMarketData(item)) {
      console.warn('待核实：后端未返回该指数有效行情字段。', {
        name: item.name,
        code: item.code,
        point: indexPointValue(item),
        todayRate: indexRateValue(item.todayRate, item),
        yearRate: indexYearRateValue(item),
        rawPoint: (item as Record<string, unknown>).point,
        rawLatest: (item as Record<string, unknown>).latest
      });
    }
  });
}

</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';

.sector-page {
  display: flex;
  flex-direction: column;
  gap: 30rpx;
  padding-top: 34rpx;
}

.stale-indicator {
  text-align: center;
  padding: 8rpx 0;
}

.stale-text {
  font-size: 22rpx;
  color: rgba(255, 200, 60, 0.8);
  letter-spacing: 1rpx;
}

.hero-card,
.board-card,
.index-card {
  background:
    radial-gradient(circle at 14% 0%, rgba(255, 95, 162, 0.12), transparent 34%),
    radial-gradient(circle at 92% 6%, rgba(56, 189, 248, 0.1), transparent 32%),
    linear-gradient(145deg, rgba(34, 49, 86, 0.58), rgba(17, 27, 52, 0.46));
}

.hero-card {
  padding: 36rpx;
}

.notice-card {
  padding: 22rpx 26rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  line-height: 1.55;
}

.hero-row,
.board-head,
.board-row,
.list-head,
.index-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
}

.list-head {
  padding: 26rpx;
  border-radius: 38rpx;
  border: 1rpx solid rgba(255, 255, 255, 0.1);
  background:
    radial-gradient(circle at 14% 0%, $soft-pink, transparent 30%),
    radial-gradient(circle at 86% 4%, $soft-cyan, transparent 30%),
    rgba(18, 28, 56, 0.7);
  box-shadow: 0 18rpx 48rpx rgba(3, 7, 18, 0.2);
}

.hero-title {
  display: block;
  margin-top: 14rpx;
  color: var(--text-primary);
  font-size: 40rpx;
  font-weight: 900;
}

.hero-subtitle,
.row-sub,
.list-subtitle,
.index-sub {
  display: block;
  margin-top: 8rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.warning-sub {
  color: #fbbf24;
}

.hero-rate {
  flex-shrink: 0;
  font-size: 46rpx;
}

.board-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}

.board-card,
.index-card {
  min-width: 0;
  padding: 26rpx;
}

.compact-title {
  font-size: 29rpx;
}

.board-row {
  padding: 18rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.12);
}

.rank-badge {
  width: 42rpx;
  height: 42rpx;
  border-radius: 20rpx;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  color: var(--button-primary-text);
  font-size: 22rpx;
  font-weight: 900;
  background: linear-gradient(135deg, rgba(255, 95, 162, 0.24), rgba(139, 92, 246, 0.18));
}

.loss-badge {
  background: linear-gradient(135deg, rgba(45, 212, 191, 0.18), rgba(56, 189, 248, 0.16));
}

.row-main {
  min-width: 0;
  flex: 1;
}

.row-title,
.index-name {
  display: block;
  color: var(--text-primary);
  font-size: 25rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row-rate {
  flex-shrink: 0;
  max-width: 120rpx;
  font-size: 25rpx;
}

.money-column,
.index-rates {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 7rpx;
  max-width: 146rpx;
  font-size: 24rpx;
  font-weight: 900;
}

.small-rate {
  font-size: 21rpx;
  font-weight: 900;
  white-space: nowrap;
}

.empty-mini {
  padding: 24rpx 0 8rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  text-align: center;
}

.index-card {
  display: flex;
  flex-direction: column;
  gap: 14rpx;
  border-radius: 36rpx;
}

.index-group {
  display: flex;
  flex-direction: column;
  gap: 16rpx;
}

.group-title {
  color: var(--text-secondary);
  font-size: 26rpx;
  font-weight: 900;
}

.index-action {
  align-self: flex-start;
  margin-top: 6rpx;
  padding: 9rpx 20rpx;
  border-radius: 999rpx;
  color: #deebff;
  background: linear-gradient(135deg, rgba(255, 95, 162, 0.14), rgba(139, 92, 246, 0.16), rgba(56, 189, 248, 0.12));
  border: 1rpx solid rgba(255, 255, 255, 0.12);
  font-size: 21rpx;
  font-weight: 900;
}

.catalog-head,
.capital-heading {
  margin-top: 2rpx;
}

.catalog-card {
  padding: 24rpx 28rpx;
}

.catalog-search,
.detail-search {
  height: 76rpx;
  padding: 0 24rpx;
  border: 1rpx solid var(--border-color);
  border-radius: 18rpx;
  color: var(--text-primary);
  background: var(--input-bg);
  font-size: 25rpx;
  box-sizing: border-box;
}

.catalog-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
  min-height: 102rpx;
  padding: 16rpx 2rpx;
  border-bottom: 1rpx solid rgba(148, 163, 184, 0.12);
}

.detail-mask {
  position: fixed;
  inset: 0;
  z-index: 1200;
  display: flex;
  align-items: flex-end;
  background: rgba(3, 7, 18, 0.68);
}

.detail-sheet {
  width: 100%;
  height: 86vh;
  padding: 28rpx 28rpx calc(30rpx + env(safe-area-inset-bottom));
  border-radius: 34rpx 34rpx 0 0;
  border-top: 1rpx solid var(--border-color);
  background: var(--page-bg);
  box-sizing: border-box;
}

.detail-head,
.detail-search-row,
.detail-fund-row {
  display: flex;
  align-items: center;
  gap: 18rpx;
}

.detail-head {
  justify-content: space-between;
  margin-bottom: 20rpx;
}

.detail-title-wrap,
.detail-fund-main {
  min-width: 0;
  flex: 1;
}

.detail-title,
.detail-sub,
.detail-fund-name,
.detail-fund-type,
.detail-quote {
  display: block;
}

.detail-title {
  color: var(--text-primary);
  font-size: 34rpx;
  font-weight: 900;
}

.detail-sub {
  margin-top: 6rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.detail-close {
  width: 64rpx;
  height: 64rpx;
  margin: 0;
  padding: 0;
  border: 0;
  border-radius: 50%;
  color: var(--text-primary);
  background: var(--control-bg);
  font-size: 40rpx;
  line-height: 64rpx;
}

.detail-tabs {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12rpx;
  margin-bottom: 16rpx;
}

.detail-tab,
.detail-search-btn,
.load-more {
  margin: 0;
  border: 1rpx solid var(--border-color);
  color: var(--text-secondary);
  background: var(--control-bg);
  font-size: 23rpx;
  font-weight: 800;
}

.detail-tab.active,
.detail-search-btn {
  color: var(--button-primary-text);
  border-color: transparent;
  background: var(--button-primary-bg);
}

.detail-search-row {
  margin-bottom: 16rpx;
}

.detail-search {
  min-width: 0;
  flex: 1;
}

.detail-search-btn {
  width: 116rpx;
  height: 76rpx;
  line-height: 76rpx;
  padding: 0;
}

.detail-list {
  height: calc(86vh - 310rpx - env(safe-area-inset-bottom));
}

.detail-fund-row {
  justify-content: space-between;
  min-height: 126rpx;
  padding: 18rpx 4rpx;
  border-bottom: 1rpx solid rgba(148, 163, 184, 0.14);
}

.detail-fund-name {
  color: var(--text-primary);
  font-size: 27rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-fund-type,
.detail-quote {
  margin-top: 5rpx;
  color: var(--text-muted);
  font-size: 20rpx;
}

.quote-live { color: #38bdf8; }
.quote-nav { color: #f59e0b; }
.quote-muted { color: var(--text-muted); }

.detail-rate-col {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8rpx;
}

.detail-rate {
  font-size: 28rpx;
  font-weight: 900;
}

.detail-month {
  font-size: 21rpx;
}

.detail-empty {
  padding: 80rpx 0;
  color: var(--text-muted);
  text-align: center;
}

.load-more {
  width: 100%;
  margin-top: 20rpx;
}
</style>
