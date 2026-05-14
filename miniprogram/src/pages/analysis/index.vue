<template>
  <view class="page-shell analysis-page">
    <view class="page-header">
      <view>
        <text class="page-title">盈亏分析</text>
        <text class="page-subtitle">{{ overview.dateText }}</text>
      </view>
      <text class="chip">{{ overview.fundCountText }} 只基金</text>
    </view>

    <view class="glass-card notice-card">
      <text>数据仅供个人记录与行情参考，不构成投资建议，实际数据以基金公司、交易所或券商披露为准。</text>
    </view>

    <view v-if="isGuest" class="glass-card empty-card">
      <text>暂无个人盈亏数据。登录后可同步你的个人持仓记录。</text>
    </view>

    <view class="glass-card overview-card">
      <view class="mode-switch">
        <button :class="['mode-button', viewMode === 'amount' ? 'active' : '']" @tap="setViewMode('amount')">金额</button>
        <button :class="['mode-button', viewMode === 'rate' ? 'active' : '']" @tap="setViewMode('rate')">收益率</button>
      </view>
      <view class="overview-head">
        <view>
          <text class="muted-text">盈亏总览</text>
          <text class="overview-title">{{ overview.statusText }}</text>
        </view>
        <text :class="['overview-profit', 'finance-number', overview.primaryClass]">
          {{ overview.primaryText }}
        </text>
      </view>
      <view class="summary-grid">
        <view class="summary-cell">
          <text class="metric-label">持仓市值</text>
          <text class="metric-value finance-number">{{ overview.totalAssetsText }}</text>
        </view>
        <view class="summary-cell">
          <text class="metric-label">{{ viewMode === 'rate' ? '今日收益率' : '今日收益' }}</text>
          <text :class="['metric-value', 'finance-number', viewMode === 'rate' ? overview.dailyRateClass : overview.dailyProfitClass]">
            {{ viewMode === 'rate' ? overview.dailyRateText : overview.dailyProfitText }}
          </text>
        </view>
        <view class="summary-cell">
          <text class="metric-label">{{ viewMode === 'rate' ? '累计收益率' : '累计盈亏' }}</text>
          <text :class="['metric-value', 'finance-number', viewMode === 'rate' ? overview.totalRateClass : overview.totalProfitClass]">
            {{ viewMode === 'rate' ? overview.totalRateText : overview.totalProfitText }}
          </text>
        </view>
        <view class="summary-cell">
          <text class="metric-label">{{ viewMode === 'rate' ? '累计盈亏' : '累计收益率' }}</text>
          <text :class="['metric-value', 'finance-number', viewMode === 'rate' ? overview.totalProfitClass : overview.totalRateClass]">
            {{ viewMode === 'rate' ? overview.totalProfitText : overview.totalRateText }}
          </text>
        </view>
      </view>
    </view>

    <view class="glass-card calendar-card">
      <view class="calendar-toolbar">
        <button class="calendar-button" @tap="goPrevMonth">上个月</button>
        <text class="section-title compact-title">{{ currentMonth }}</text>
        <button class="calendar-button" @tap="goNextMonth">下个月</button>
        <button class="calendar-button today-button" @tap="goToday">今天</button>
      </view>
      <view class="weekday-row">
        <text v-for="item in weekDays" :key="item">{{ item }}</text>
      </view>
      <view class="calendar-grid">
        <view v-for="blank in leadingBlanks" :key="blank" class="calendar-blank" />
        <button
          v-for="day in calendarDays"
          :key="day.key"
          :class="['calendar-day', day.selected ? 'selected' : '', day.hasData ? '' : 'empty-day']"
          @tap="selectDate(day.date)"
        >
          <text class="day-number">{{ day.dayText }}</text>
          <text :class="['day-profit', 'finance-number', day.profitClass]">{{ day.profitText }}</text>
        </button>
      </view>
    </view>

    <view class="top-grid">
      <view class="glass-card rank-card">
        <text class="section-title compact-title">{{ viewMode === 'rate' ? '收益率 TOP5' : '盈利 TOP5' }}</text>
        <view v-if="profitTop.length === 0" class="empty-mini">暂无盈利数据</view>
        <view v-for="item in profitTop" :key="item.key" class="rank-row">
          <view class="rank-name">
            <text>{{ item.nameText }}</text>
            <text>{{ item.codeText }}</text>
          </view>
          <text class="rank-amount finance-number profit-text">{{ rankText(item) }}</text>
        </view>
      </view>

      <view class="glass-card rank-card">
        <text class="section-title compact-title">{{ viewMode === 'rate' ? '亏损率 TOP5' : '亏损 TOP5' }}</text>
        <view v-if="lossTop.length === 0" class="empty-mini">暂无亏损数据</view>
        <view v-for="item in lossTop" :key="item.key" class="rank-row">
          <view class="rank-name">
            <text>{{ item.nameText }}</text>
            <text>{{ item.codeText }}</text>
          </view>
          <text class="rank-amount finance-number loss-text">{{ rankText(item) }}</text>
        </view>
      </view>
    </view>

    <view class="glass-card detail-card">
      <view class="detail-head">
        <view>
          <text class="section-title">当日明细</text>
          <text class="list-subtitle">{{ selectedDate }} · 点击日历切换</text>
        </view>
        <text class="muted-text">{{ selectedDayRows.length }} 条</text>
      </view>

      <view v-if="selectedDayRows.length === 0" class="empty-card inner-empty" @tap="!isGuest && loadData(true)">
        <text>{{ isGuest ? '登录后可同步你的个人持仓记录。' : '该日暂无基金明细，点击重试或下拉刷新' }}</text>
      </view>

      <view v-for="item in selectedDayRows" :key="item.key" class="detail-row">
        <view class="detail-main">
          <text class="fund-name">{{ item.nameText }}</text>
          <text class="fund-code">{{ item.codeText }}</text>
        </view>
        <view class="detail-values">
          <text :class="['finance-number', rankClass(item)]">{{ rankText(item) }}</text>
          <text class="small-rate">{{ viewMode === 'rate' ? item.dailyProfitText : item.dailyRateText }}</text>
        </view>
      </view>
    </view>

    <view class="safe-tabbar-space" />
    <AppTabBar active="analysis" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import AppTabBar from '../../components/AppTabBar.vue';
import { getArchives, getInsightsDashboard, type ArchiveRow, type InsightsDashboard } from '../../services/api/analysis';
import { loadSession, sessionState } from '../../stores/session';

interface DetailView {
  key: string;
  date: string;
  codeKey: string;
  nameText: string;
  codeText: string;
  dailyProfit: number | null;
  dailyProfitText: string;
  dailyProfitClass: string;
  dailyRate: number | null;
  dailyRateText: string;
  dailyRateClass: string;
  totalProfit: number | null;
  totalRate: number | null;
}

const weekDays = ['一', '二', '三', '四', '五', '六', '日'];
const loading = ref(false);
const dashboard = ref<InsightsDashboard>({});
const archives = ref<ArchiveRow[]>([]);
const selectedDate = ref(todayDate());
const currentMonth = ref(todayDate().slice(0, 7));
const viewMode = ref<'amount' | 'rate'>('amount');
const isGuest = computed(() => !sessionState.username);

const fundRows = computed(() =>
  archives.value
    .filter((row) => !isTotalRow(row))
    .map((row, index) => normalizeDetailRow(row, index))
    .filter((row) => Boolean(row.date))
);
const totalRows = computed(() => archives.value.filter(isTotalRow));
const selectedDayRows = computed(() => fundRows.value.filter((row) => row.date === selectedDate.value));
const selectedTotal = computed(() => totalRows.value.find((row) => normalizeDate(row.recordDate) === selectedDate.value) || null);
const leadingBlanks = computed(() => {
  const [year, month] = currentMonth.value.split('-').map((item) => Number(item));
  const first = new Date(year, month - 1, 1).getDay();
  const count = first === 0 ? 6 : first - 1;
  return Array.from({ length: count }, (_, index) => `blank-${currentMonth.value}-${index}`);
});
const totalByDate = computed(() => {
  const map = new Map<string, ArchiveRow>();
  for (const row of totalRows.value) {
    const date = normalizeDate(row.recordDate);
    if (date) map.set(date, row);
  }
  return map;
});
const calendarDays = computed(() => {
  const [year, month] = currentMonth.value.split('-').map((item) => Number(item));
  const lastDay = new Date(year, month, 0).getDate();
  return Array.from({ length: lastDay }, (_, index) => {
    const day = index + 1;
    const date = `${currentMonth.value}-${String(day).padStart(2, '0')}`;
    const row = totalByDate.value.get(date);
    const profit = viewMode.value === 'rate' ? finiteNumber(row?.dailyRate) : finiteNumber(row?.dailyProfit);
    return {
      key: `day-${date}`,
      date,
      dayText: String(day),
      selected: date === selectedDate.value,
      hasData: Boolean(row),
      profitText: row ? (viewMode.value === 'rate' ? signedPercentDash(profit) : signedMoneyDash(profit)) : '--',
      profitClass: profitClass(profit)
    };
  });
});
const overview = computed(() => {
  const dailyReport = dashboard.value.dailyReport || {};
  const total = selectedTotal.value;
  const totalAssets = firstFinite(total?.assets, dailyReport.totalAssets);
  const dailyProfit = firstFinite(total?.dailyProfit, dailyReport.dailyProfit);
  const dailyRate = firstFinite(total?.dailyRate, dailyReport.dailyRate);
  const totalProfit = firstFinite(total?.totalProfit, dailyReport.totalProfit);
  const totalRate = firstFinite(total?.totalRate);
  const fundCount = firstFinite(dailyReport.fundCount, selectedDayRows.value.length);

  return {
    dateText: `${selectedDate.value} · 下拉刷新`,
    statusText: selectedTotal.value ? '已归档收盘数据' : '暂无当日总计',
    fundCountText: fundCount === null ? '--' : String(fundCount),
    totalAssetsText: moneyDash(totalAssets),
    dailyProfitText: signedMoneyDash(dailyProfit),
    dailyProfitClass: profitClass(dailyProfit),
    dailyRateText: signedPercentDash(dailyRate),
    dailyRateClass: profitClass(dailyRate),
    totalProfitText: signedMoneyDash(totalProfit),
    totalProfitClass: profitClass(totalProfit),
    totalRateText: signedPercentDash(totalRate),
    totalRateClass: profitClass(totalRate),
    primaryText: viewMode.value === 'rate' ? signedPercentDash(dailyRate) : signedMoneyDash(dailyProfit),
    primaryClass: viewMode.value === 'rate' ? profitClass(dailyRate) : profitClass(dailyProfit)
  };
});
const profitTop = computed(() =>
  dedupeByFund(selectedDayRows.value)
    .filter((row) => rankValue(row) !== null && Number(rankValue(row)) > 0)
    .sort((a, b) => Number(rankValue(b)) - Number(rankValue(a)))
    .slice(0, 5)
);
const lossTop = computed(() =>
  dedupeByFund(selectedDayRows.value)
    .filter((row) => rankValue(row) !== null && Number(rankValue(row)) < 0)
    .sort((a, b) => Number(rankValue(a)) - Number(rankValue(b)))
    .slice(0, 5)
);

onShow(() => {
  loadSession();
  if (!sessionState.username) {
    dashboard.value = {};
    archives.value = [];
    return;
  }

  loadData(false).catch((error) => console.warn('[analysis:load]', error));
});

onPullDownRefresh(async () => {
  try {
    await loadData(true);
  } catch (error) {
    console.warn('[analysis:pull-down-refresh]', error);
    uni.showToast({ title: '刷新失败，请稍后重试', icon: 'none' });
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadData(force = false) {
  if (loading.value) return;
  if (!sessionState.username) {
    dashboard.value = {};
    archives.value = [];
    return;
  }

  loading.value = true;
  try {
    const [insights, rows] = await Promise.all([getInsightsDashboard(sessionState.username), getArchives(sessionState.username, 500)]);
    dashboard.value = insights || {};
    archives.value = Array.isArray(rows) ? rows : [];
    if (force || archives.value.length > 0) ensureSelectedDate();
  } finally {
    loading.value = false;
  }
}

function ensureSelectedDate() {
  const dates = Array.from(totalByDate.value.keys()).sort((a, b) => b.localeCompare(a));
  const today = todayDate();
  const next = dates.includes(today) ? today : dates[0] || today;
  selectedDate.value = next;
  currentMonth.value = next.slice(0, 7);
}

function selectDate(date: string) {
  selectedDate.value = date;
}

function setViewMode(mode: 'amount' | 'rate') {
  viewMode.value = mode;
}

function goPrevMonth() {
  shiftMonth(-1);
}

function goNextMonth() {
  shiftMonth(1);
}

function goToday() {
  const today = todayDate();
  selectedDate.value = today;
  currentMonth.value = today.slice(0, 7);
}

function shiftMonth(delta: number) {
  const [year, month] = currentMonth.value.split('-').map((item) => Number(item));
  const next = new Date(year, month - 1 + delta, 1);
  currentMonth.value = `${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, '0')}`;
}

function todayDate() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

function normalizeDate(value: unknown) {
  return value ? String(value).slice(0, 10) : '';
}

function isTotalRow(row: ArchiveRow) {
  return String(row.fundCode || '').toUpperCase() === 'TOTAL';
}

function normalizeDetailRow(row: ArchiveRow, index: number): DetailView {
  const source = row as Record<string, unknown>;
  const date = normalizeDate(row.recordDate);
  const code = String(row.fundCode || source.code || source.symbol || '').trim();
  const name = String(row.fundName || source.name || '').trim();
  const keyBase = code || name || 'fund';
  const dailyProfit = finiteNumber(row.dailyProfit);
  const dailyRate = finiteNumber(row.dailyRate);
  const totalProfit = finiteNumber(row.totalProfit);
  const totalRate = finiteNumber(row.totalRate);

  return {
    key: `${keyBase}-${date}-${index}`,
    date,
    codeKey: keyBase,
    nameText: name || code || '--',
    codeText: code || '--',
    dailyProfit,
    dailyProfitText: signedMoneyDash(dailyProfit),
    dailyProfitClass: profitClass(dailyProfit),
    dailyRate,
    dailyRateText: signedPercentDash(dailyRate),
    dailyRateClass: profitClass(dailyRate),
    totalProfit,
    totalRate
  };
}

function dedupeByFund(rows: DetailView[]) {
  const map = new Map<string, DetailView>();
  for (const row of rows) {
    const key = row.codeKey || row.nameText;
    const prev = map.get(key);
    if (!prev) {
      map.set(key, row);
      continue;
    }

    const prevRank = rankValue(prev);
    const nextRank = rankValue(row);
    const prevValue = prevRank === null ? -Infinity : Math.abs(prevRank);
    const nextValue = nextRank === null ? -Infinity : Math.abs(nextRank);
    if (nextValue > prevValue) map.set(key, row);
  }

  return Array.from(map.values());
}

function rankValue(row: DetailView) {
  return viewMode.value === 'rate' ? row.dailyRate : row.dailyProfit;
}

function rankText(row: DetailView) {
  return viewMode.value === 'rate' ? row.dailyRateText : row.dailyProfitText;
}

function rankClass(row: DetailView) {
  return viewMode.value === 'rate' ? row.dailyRateClass : row.dailyProfitClass;
}

function finiteNumber(value: unknown) {
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function firstFinite(...values: unknown[]) {
  for (const value of values) {
    const n = finiteNumber(value);
    if (n !== null) return n;
  }
  return null;
}

function moneyDash(value: number | null) {
  return value === null ? '--' : `¥\u00a0${value.toFixed(2)}`;
}

function signedMoneyDash(value: number | null) {
  if (value === null) return '--';
  return `${value >= 0 ? '+' : '-'}¥\u00a0${Math.abs(value).toFixed(2)}`;
}

function signedPercentDash(value: number | null) {
  if (value === null) return '--';
  return `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`;
}

function profitClass(value: number | null) {
  if (value === null) return '';
  return value >= 0 ? 'profit-text' : 'loss-text';
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';

.analysis-page {
  display: flex;
  flex-direction: column;
  gap: 26rpx;
  padding-top: 34rpx;
}

.overview-card,
.calendar-card,
.rank-card,
.detail-card {
  background:
    radial-gradient(circle at 10% 8%, rgba(96, 165, 250, 0.2), transparent 34%),
    linear-gradient(135deg, rgba(30, 41, 59, 0.72), rgba(30, 27, 75, 0.58));
}

.overview-card,
.calendar-card,
.rank-card,
.detail-card {
  padding: 28rpx;
}

.notice-card {
  padding: 22rpx 26rpx;
  color: $text-muted;
  font-size: 22rpx;
  line-height: 1.55;
}

.mode-switch {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12rpx;
  margin-bottom: 24rpx;
  padding: 8rpx;
  border-radius: 999rpx;
  background: rgba(15, 23, 42, 0.45);
  border: 1rpx solid rgba(148, 163, 184, 0.14);
}

.mode-button {
  height: 60rpx;
  border-radius: 999rpx;
  color: $text-muted;
  background: transparent;
  font-size: 24rpx;
  font-weight: 900;
  line-height: 60rpx;
}

.mode-button.active {
  color: #fff;
  background: linear-gradient(135deg, $primary-blue, $primary-purple);
  box-shadow: 0 12rpx 28rpx rgba(59, 130, 246, 0.22);
}

.overview-head,
.detail-head,
.rank-row,
.detail-row,
.calendar-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18rpx;
}

.overview-title,
.list-subtitle {
  display: block;
  margin-top: 8rpx;
  color: $text-muted;
  font-size: 22rpx;
}

.overview-profit {
  flex-shrink: 0;
  font-size: 42rpx;
  font-weight: 900;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16rpx;
  margin-top: 26rpx;
}

.summary-cell {
  min-width: 0;
  padding: 20rpx;
  border-radius: 22rpx;
  background: rgba(15, 23, 42, 0.42);
  border: 1rpx solid rgba(148, 163, 184, 0.12);
}

.metric-label {
  display: block;
  color: $text-muted;
  font-size: 22rpx;
}

.metric-value {
  display: inline-flex;
  max-width: 100%;
  margin-top: 10rpx;
  font-size: 29rpx;
  font-weight: 900;
  white-space: nowrap;
  word-break: keep-all;
}

.calendar-toolbar {
  flex-wrap: nowrap;
}

.compact-title {
  font-size: 28rpx;
}

.calendar-button {
  min-width: 112rpx;
  height: 58rpx;
  padding: 0 14rpx;
  border-radius: 999rpx;
  color: $text-soft;
  background: rgba(96, 165, 250, 0.12);
  border: 1rpx solid rgba(96, 165, 250, 0.22);
  font-size: 22rpx;
  font-weight: 900;
  line-height: 58rpx;
}

.today-button {
  min-width: 88rpx;
}

.weekday-row,
.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 10rpx;
}

.weekday-row {
  margin-top: 22rpx;
  color: $text-muted;
  text-align: center;
  font-size: 20rpx;
}

.calendar-grid {
  margin-top: 12rpx;
}

.calendar-blank,
.calendar-day {
  min-height: 86rpx;
}

.calendar-day {
  padding: 8rpx 4rpx;
  border-radius: 18rpx;
  background: rgba(15, 23, 42, 0.38);
  border: 1rpx solid rgba(148, 163, 184, 0.12);
  color: $text-soft;
}

.calendar-day.selected {
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.72), rgba(139, 92, 246, 0.7));
  border-color: rgba(191, 219, 254, 0.34);
}

.empty-day {
  opacity: 0.62;
}

.day-number,
.day-profit {
  display: block;
  text-align: center;
  white-space: nowrap;
}

.day-number {
  font-size: 22rpx;
  font-weight: 900;
}

.day-profit {
  margin-top: 6rpx;
  font-size: 17rpx;
  transform: scale(0.86);
}

.top-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 18rpx;
}

.rank-row {
  margin-top: 18rpx;
  padding-top: 16rpx;
  border-top: 1rpx solid rgba(148, 163, 184, 0.12);
}

.rank-name,
.detail-main {
  flex: 1 1 auto;
  min-width: 0;
}

.rank-amount {
  flex: 0 0 190rpx;
  max-width: 220rpx;
  min-width: 170rpx;
  text-align: right;
  font-size: 28rpx;
  font-weight: 900;
  line-height: 1.18;
  white-space: nowrap;
  word-break: keep-all;
  overflow: visible;
}

.rank-name text:first-child,
.fund-name {
  display: block;
  color: $text-white;
  font-size: 24rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rank-name text:last-child,
.fund-code {
  display: block;
  margin-top: 6rpx;
  color: $text-muted;
  font-size: 20rpx;
}

.empty-mini {
  padding: 26rpx 0 4rpx;
  color: $text-muted;
  font-size: 22rpx;
  text-align: center;
}

.inner-empty {
  margin-top: 18rpx;
  padding: 32rpx 20rpx;
}

.detail-row {
  padding: 20rpx 0;
  border-top: 1rpx solid rgba(148, 163, 184, 0.12);
}

.detail-values {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 6rpx;
  max-width: 210rpx;
}

.small-rate {
  font-size: 22rpx;
  font-weight: 900;
  white-space: nowrap;
}
</style>
