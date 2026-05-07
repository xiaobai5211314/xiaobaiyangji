<template>
  <view class="page-shell sector-page">
    <view class="page-header">
      <view>
        <text class="page-title">板块雷达</text>
        <text class="page-subtitle">{{ updatedAtText }}</text>
      </view>
      <text class="chip">{{ sectorCount }} 个主题</text>
    </view>

    <view class="glass-card hero-card">
      <text class="muted-text">今日领涨</text>
      <view class="hero-row">
        <view>
          <text class="hero-title">{{ topList[0]?.name || '暂无板块数据' }}</text>
          <text class="hero-subtitle">{{ sectorPayload.source || '板块基金池 · 实时估值均值' }}</text>
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
        <view v-for="(item, index) in topList.slice(0, 8)" :key="sectorKey(item, index, 'top')" class="board-row">
          <text class="rank-badge">{{ index + 1 }}</text>
          <view class="row-main">
            <text class="row-title">{{ item.name || '未知板块' }}</text>
            <text class="row-sub">{{ sectorFundCount(item) }} 只基金</text>
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
        <view v-for="(item, index) in bottomList.slice(0, 8)" :key="sectorKey(item, index, 'bottom')" class="board-row">
          <text class="rank-badge loss-badge">{{ index + 1 }}</text>
          <view class="row-main">
            <text class="row-title">{{ item.name || '未知板块' }}</text>
            <text class="row-sub">{{ sectorFundCount(item) }} 只基金</text>
          </view>
          <text :class="['row-rate', 'finance-number', optionalProfitClass(item.rate)]">{{ signedOptionalPercent(item.rate) }}</text>
        </view>
      </view>
    </view>

    <view class="board-grid">
      <view class="glass-card board-card">
        <view class="board-head">
          <text class="section-title compact-title">流入榜</text>
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
          <text class="section-title compact-title">流出榜</text>
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

    <view v-if="visibleIndices.length === 0 && !loading" class="glass-card empty-card">
      <text>暂无大盘指数数据</text>
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

    <view class="safe-tabbar-space" />
    <AppTabBar active="sector" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import AppTabBar from '../../components/AppTabBar.vue';
import {
  getCapitalFlow,
  getGlobalIndices,
  getSectors,
  type CapitalFlowResponse,
  type CapitalFlowRow,
  type GlobalIndexItem,
  type SectorRadarResponse,
  type SectorSummary
} from '../../services/api/sector';
import { optionalProfitClass, signedMoney, signedPercent } from '../../utils/format';

const loading = ref(false);
const sectorPayload = ref<SectorRadarResponse>({});
const flowPayload = ref<CapitalFlowResponse>({});
const indices = ref<GlobalIndexItem[]>([]);
const DEBUG_FIELD_AUDIT =
  (import.meta as ImportMeta & { env?: { DEV?: boolean } }).env?.DEV !== false;

const allSectors = computed(() => sectorPayload.value.all || []);
const topList = computed(() => {
  const source = sectorPayload.value.top?.length ? sectorPayload.value.top : allSectors.value;
  return [...source].sort((a, b) => Number(b.rate || 0) - Number(a.rate || 0));
});
const bottomList = computed(() => {
  const source = sectorPayload.value.bottom?.length ? sectorPayload.value.bottom : allSectors.value;
  return [...source].sort((a, b) => Number(a.rate || 0) - Number(b.rate || 0));
});
const flowRows = computed(() => flowPayload.value.rows || []);
const inflowList = computed(() => {
  const source = flowPayload.value.inflow?.length ? flowPayload.value.inflow : flowRows.value;
  return [...source].sort((a, b) => Number(b.mainNet || 0) - Number(a.mainNet || 0));
});
const outflowList = computed(() => {
  const source = flowPayload.value.outflow?.length ? flowPayload.value.outflow : flowRows.value;
  return [...source].sort((a, b) => Number(a.mainNet || 0) - Number(b.mainNet || 0));
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
  loadData(false).catch((error) => console.error('[sector:load]', error));
});

onPullDownRefresh(async () => {
  try {
    await loadData(true);
  } catch (error) {
    console.error('[sector:pull-down-refresh]', error);
    uni.showToast({ title: '刷新失败，请稍后重试', icon: 'none' });
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadData(force: boolean) {
  if (loading.value) return;
  loading.value = true;
  try {
    const [sectors, flow, globalIndexRows] = await Promise.all([getSectors(force), getCapitalFlow(force, 30), getGlobalIndices()]);
    sectorPayload.value = sectors || {};
    flowPayload.value = flow || {};
    indices.value = Array.isArray(globalIndexRows) ? globalIndexRows : [];
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
  return firstNumber(
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

function firstArray(...values: unknown[]) {
  for (const value of values) {
    if (Array.isArray(value)) return value;
  }

  return [];
}

function logGlobalIndicesAudit(rows: GlobalIndexItem[]) {
  if (!DEBUG_FIELD_AUDIT) return;

  rows.forEach((item) => {
    console.log('[global.indices keys]', item.name, item.code, Object.keys(item));
    console.log('[global.indices fields]', {
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

.hero-card,
.board-card,
.index-card {
  background:
    radial-gradient(circle at 12% 8%, rgba(90, 167, 255, 0.16), transparent 36%),
    linear-gradient(145deg, rgba(34, 49, 86, 0.58), rgba(17, 27, 52, 0.46));
}

.hero-card {
  padding: 36rpx;
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

.hero-title {
  display: block;
  margin-top: 14rpx;
  color: $text-white;
  font-size: 40rpx;
  font-weight: 900;
}

.hero-subtitle,
.row-sub,
.list-subtitle,
.index-sub {
  display: block;
  margin-top: 8rpx;
  color: $text-muted;
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
  border-radius: 18rpx;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  color: #fff;
  font-size: 22rpx;
  font-weight: 900;
  background: rgba(255, 107, 107, 0.18);
}

.loss-badge {
  background: rgba(45, 212, 191, 0.18);
}

.row-main {
  min-width: 0;
  flex: 1;
}

.row-title,
.index-name {
  display: block;
  color: $text-white;
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
  color: $text-muted;
  font-size: 22rpx;
  text-align: center;
}

.index-card {
  display: flex;
  flex-direction: column;
  gap: 14rpx;
}

.index-group {
  display: flex;
  flex-direction: column;
  gap: 16rpx;
}

.group-title {
  color: $text-soft;
  font-size: 26rpx;
  font-weight: 900;
}

.index-action {
  align-self: flex-start;
  margin-top: 6rpx;
  padding: 9rpx 20rpx;
  border-radius: 999rpx;
  color: #deebff;
  background: rgba(90, 167, 255, 0.12);
  border: 1rpx solid rgba(191, 219, 254, 0.14);
  font-size: 21rpx;
  font-weight: 900;
}
</style>
