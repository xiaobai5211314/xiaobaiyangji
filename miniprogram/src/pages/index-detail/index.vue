<template>
  <view :class="['page-shell', 'index-detail-page', themeClass]">
    <view class="page-header">
      <view>
        <text class="page-title">{{ displayIndexName(currentIndex) || '指数详情' }}</text>
        <text class="page-subtitle">近 1 年历史数据</text>
      </view>
      <button class="back-button" @tap="goBack">返回</button>
    </view>

    <view v-if="!currentIndex.name && !loading" class="glass-card empty-card">
      <text>暂无指数数据</text>
    </view>

    <view v-else class="glass-card detail-card">
      <view class="detail-head">
        <view>
          <text class="muted-text">当前点位</text>
          <text class="index-latest finance-number">{{ indexPointText(currentIndex) }}</text>
        </view>
        <view class="rate-column">
          <text :class="['finance-number', optionalProfitClass(indexRateValue(currentIndex.todayRate, currentIndex))]">今 {{ indexPercentText(currentIndex.todayRate, currentIndex) }}</text>
          <text :class="['small-rate', optionalProfitClass(indexYearRateValue(currentIndex))]">1年 {{ indexYearPercentText(currentIndex) }}</text>
        </view>
      </view>

      <view class="history-block">
        <view class="history-head">
          <text>近 1 年数据</text>
          <text>{{ historyRows.length }} 条</text>
        </view>
        <view class="table-head">
          <text>日期</text>
          <text>指数点位</text>
          <text>涨跌幅</text>
        </view>
        <view v-if="historyRows.length === 0" class="empty-history">暂无近一年历史数据</view>
        <view v-for="(row, index) in historyRows" :key="historyKey(row, index)" class="history-row">
          <text>{{ row.dateText }}</text>
          <text class="finance-number">{{ row.closeText }}</text>
          <text :class="['finance-number', optionalProfitClass(row.rate)]">{{ row.rateText }}</text>
        </view>
      </view>

      <text v-if="currentIndex.updatedAt" class="updated-text">更新 {{ currentIndex.updatedAt }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad, onPullDownRefresh } from '@dcloudio/uni-app';
import { getGlobalIndices, type GlobalIndexItem, type GlobalIndexKline } from '../../services/api/sector';
import { optionalProfitClass, signedPercent } from '../../utils/format';
import { loadTheme, themeClass } from '../../stores/theme';

interface HistoryRow {
  raw: GlobalIndexKline;
  dateText: string;
  closeText: string;
  rate: number | null;
  rateText: string;
  pointSource: string;
}

const loading = ref(false);
const indexName = ref('');
const indexCode = ref('');
const rows = ref<GlobalIndexItem[]>([]);

const currentIndex = computed(() => {
  const name = indexName.value;
  const code = indexCode.value;
  return (
    rows.value.find((item) => String(item.code || '') === code && code) ||
    rows.value.find((item) => String(item.name || '') === name && name) ||
    rows.value.find((item) => cleanIndexName(item.name) === cleanIndexName(name) && name) ||
    {}
  );
});
const historyRows = computed(() => indexHistoryRows(currentIndex.value));

onLoad((query) => {
  loadTheme();
  indexName.value = decodeURIComponent(String(query?.indexName || ''));
  indexCode.value = decodeURIComponent(String(query?.indexCode || ''));
  loadData(false).catch((error) => console.warn('[index-detail:load]', error));
});

onPullDownRefresh(async () => {
  try {
    await loadData(true);
  } catch (error) {
    console.warn('[index-detail:pull-down-refresh]', error);
    uni.showToast({ title: '刷新失败，请稍后重试', icon: 'none' });
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadData(force = false) {
  if (loading.value) return;
  loading.value = true;
  try {
    const data = await getGlobalIndices(force);
    rows.value = Array.isArray(data) ? data.filter(hasIndexEntry) : [];
  } finally {
    loading.value = false;
  }
}

function indexHistoryRows(item: GlobalIndexItem) {
  const rows = getHistoryList(item);
  const sortedRows = [...rows].sort((a, b) => normalizeDateText(b).localeCompare(normalizeDateText(a)));
  let derivedClose = indexPointValue(item);

  return sortedRows
    .map((row) => {
      const normalized = normalizeIndexHistoryItem(row, derivedClose);
      if (normalized.close !== null) {
        const rateForPrev = normalized.rate;
        if (rateForPrev !== null && rateForPrev !== -100) {
          derivedClose = normalized.close / (1 + rateForPrev / 100);
        } else {
          derivedClose = null;
        }
      }

      return {
        raw: row,
        dateText: normalized.dateText,
        closeText: normalized.close === null ? '--' : normalized.close.toFixed(2),
        rate: normalized.rate,
        rateText: normalized.rate === null ? '--' : signedPercent(normalized.rate),
        pointSource: normalized.pointSource
      };
    })
    .filter((row) => row.dateText !== '--' || row.rate !== null);
}

function hasIndexEntry(item: GlobalIndexItem) {
  const name = String(item.name || '');
  return Boolean(cleanIndexName(name));
}

function signedOptionalPercent(value: unknown) {
  if (value === null || value === undefined || value === '') return '--';
  return signedPercent(value);
}

function numericOrDash(value: unknown) {
  const n = Number(value);
  return Number.isFinite(n) ? n.toFixed(2) : '--';
}

function finiteNumber(value: unknown) {
  if (!isPresent(value)) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function normalizeIndexHistoryItem(row: GlobalIndexKline, fallbackClose: number | null) {
  const source = row as Record<string, unknown>;
  const directClose = firstIndexPoint(
    source.point,
    source.latest,
    source.close,
    source.value,
    source.indexValue,
    source.current,
    source.price
  );
  const rate = firstFinite(source.todayRate, source.rate, source.changePercent, source.pct, source.pctChg);
  const dateText = String(
    firstKnown(source.date, source.tradeDate, source.time, source.day, source.datetime) || '--'
  ).slice(0, 10);

  if (directClose !== null) {
    return { dateText, close: directClose, rate, pointSource: 'direct' };
  }

  return {
    dateText,
    close: fallbackClose,
    rate,
    pointSource: fallbackClose === null ? 'missing' : 'derivedFromLatestAndRate'
  };
}

function normalizeDateText(row: GlobalIndexKline) {
  const source = row as Record<string, unknown>;
  return String(firstKnown(source.date, source.tradeDate, source.time, source.day, source.datetime) || '');
}

function cleanIndexName(value: unknown) {
  return String(value || '').replace(/\s*\((?:无数据|异常:.*)\)\s*$/g, '').trim();
}

function displayIndexName(item: GlobalIndexItem) {
  return cleanIndexName(item.name);
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
  return firstFinite(value, source.rate, source.changePercent, source.pct, source.pctChg);
}

function indexPercentText(value: unknown, item: GlobalIndexItem) {
  const n = indexRateValue(value, item);
  return n === null ? '--' : signedPercent(n);
}

function indexPointValue(item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  return firstIndexPoint(
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
  return firstFinite(source.yearRate, source.oneYearRate, source.annualRate, source.yearChangePercent);
}

function indexYearPercentText(item: GlobalIndexItem) {
  const n = indexYearRateValue(item);
  return n === null ? '--' : signedPercent(n);
}

function getHistoryList(item: GlobalIndexItem) {
  const source = item as Record<string, unknown>;
  const rows = firstArray(source.klines, source.lines, source.history, source.series, source.data, source.items, source.list);
  return rows as GlobalIndexKline[];
}

function firstFinite(...values: unknown[]) {
  for (const value of values) {
    const n = finiteNumber(value);
    if (n !== null) return n;
  }
  return null;
}

function firstIndexPoint(...values: unknown[]) {
  for (const value of values) {
    const n = finiteNumber(value);
    if (n !== null && n > 0) return n;
  }
  return null;
}

function firstKnown(...values: unknown[]) {
  for (const value of values) {
    if (value !== null && value !== undefined && value !== '') return value;
  }
  return null;
}

function firstArray(...values: unknown[]) {
  for (const value of values) {
    if (Array.isArray(value)) return value;
  }

  return [];
}

function isPresent(value: unknown) {
  return value !== null && value !== undefined && !(typeof value === 'string' && value.trim() === '');
}

function historyKey(row: HistoryRow, index: number) {
  return `${row.dateText}-${index}`;
}

function goBack() {
  uni.navigateBack({
    fail: () => uni.reLaunch({ url: '/pages/sector/index' })
  });
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';

.index-detail-page {
  display: flex;
  flex-direction: column;
  gap: 28rpx;
  padding-top: 34rpx;
}

.back-button {
  min-width: 104rpx;
  height: 56rpx;
  border-radius: 999rpx;
  color: $text-soft;
  background: rgba(148, 163, 184, 0.12);
  border: 1rpx solid rgba(148, 163, 184, 0.16);
  font-size: 24rpx;
  font-weight: 900;
  line-height: 56rpx;
}

.detail-card {
  padding: 34rpx;
  background:
    radial-gradient(circle at 12% 8%, rgba(96, 165, 250, 0.22), transparent 34%),
    linear-gradient(135deg, rgba(30, 41, 59, 0.76), rgba(30, 27, 75, 0.58));
}

.detail-head,
.history-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
}

.index-latest {
  display: block;
  margin-top: 12rpx;
  color: $text-white;
  font-size: 52rpx;
  font-weight: 900;
}

.rate-column {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8rpx;
  font-size: 28rpx;
  font-weight: 900;
}

.small-rate {
  font-size: 23rpx;
  font-weight: 900;
  white-space: nowrap;
}

.history-block {
  margin-top: 34rpx;
  padding: 20rpx;
  border-radius: 24rpx;
  background: rgba(15, 23, 42, 0.36);
  border: 1rpx solid rgba(96, 165, 250, 0.14);
}

.history-head {
  margin-bottom: 14rpx;
  color: $text-muted;
  font-size: 23rpx;
}

.history-head text:first-child {
  color: $text-soft;
  font-weight: 900;
}

.table-head,
.history-row {
  display: grid;
  grid-template-columns: 1.2fr 1fr 1fr;
  gap: 14rpx;
  align-items: center;
}

.table-head {
  padding: 12rpx 0;
  color: $text-muted;
  font-size: 21rpx;
}

.history-row {
  padding: 16rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.12);
  color: $text-soft;
  font-size: 23rpx;
}

.history-row text:nth-child(2),
.history-row text:nth-child(3),
.table-head text:nth-child(2),
.table-head text:nth-child(3) {
  text-align: right;
}

.empty-history {
  padding: 38rpx 0;
  color: $text-muted;
  text-align: center;
  font-size: 24rpx;
}

.updated-text {
  display: block;
  margin-top: 22rpx;
  color: $text-muted;
  font-size: 22rpx;
}
</style>
