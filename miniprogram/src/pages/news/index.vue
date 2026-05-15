<template>
  <view :class="['page-shell', 'news-page', themeClass]">
    <view class="page-header">
      <view>
        <text class="page-title">资讯雷达</text>
        <text class="page-subtitle">{{ updatedAtText }}</text>
      </view>
      <text class="chip">{{ activeItems.length }} 条</text>
    </view>

    <view class="glass-card switch-card">
      <button :class="['switch-btn', mode === 'global' ? 'active' : '']" @tap="mode = 'global'">7x24</button>
      <button :class="['switch-btn', mode === 'holding' ? 'active' : '']" @tap="mode = 'holding'">持仓相关</button>
    </view>

    <view class="glass-card notice-card">
      <view class="notice-text">数据仅供个人记录与行情参考，不构成投资建议，实际数据以基金公司、交易所或券商披露为准。</view>
    </view>

    <view v-if="activeItems.length === 0 && !loading" class="glass-card empty-card" @tap="loadData(true)">
      <text>{{ mode === 'holding' && !sessionState.username ? '登录后可同步你的个人持仓记录。' : '暂无资讯数据，点击重试或下拉刷新' }}</text>
    </view>

    <view v-for="(item, itemIndex) in activeItems" :key="newsItemKey(item, itemIndex)" class="glass-card news-card">
      <view class="news-head">
        <view class="time-dot" />
        <view class="news-main">
          <view class="news-meta">
            <text>{{ item.timeText || item.dateText || item.showTime || '刚刚' }}</text>
            <text>{{ item.source || '东方财富' }}</text>
          </view>
          <text class="news-title">{{ item.title || '暂无标题' }}</text>
          <text v-if="item.summary" class="news-summary">{{ item.summary }}</text>
          <view class="tag-row">
            <text v-if="item.important" class="tag important">重要</text>
            <text v-if="item.sentiment" class="tag">{{ item.sentiment }}</text>
            <text v-if="item.matchedFundName" class="tag">{{ item.matchedFundName }}</text>
            <text v-for="(tag, tagIndex) in item.tags || []" :key="`${newsItemKey(item, itemIndex)}-${tag}-${tagIndex}`" class="tag">{{ tag }}</text>
          </view>
        </view>
      </view>
    </view>

    <view class="safe-tabbar-space" />
    <AppTabBar active="news" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import AppTabBar from '../../components/AppTabBar.vue';
import { getGlobalNews, getHoldingNews, type NewsItem, type NewsResponse } from '../../services/api/news';
import { restoreSession, sessionState } from '../../stores/session';
import { loadTheme, themeClass } from '../../stores/theme';

const loading = ref(false);
const mode = ref<'global' | 'holding'>('global');
const globalPayload = ref<NewsResponse>({});
const holdingPayload = ref<NewsResponse>({});
const PAGE_CACHE_TTL = 60000;
const loadedAt = ref(0);

const activeItems = computed<NewsItem[]>(() => {
  const rows = mode.value === 'holding' ? holdingPayload.value.items : globalPayload.value.items;
  return Array.isArray(rows) ? rows : [];
});
const updatedAtText = computed(() => {
  const payload = mode.value === 'holding' ? holdingPayload.value : globalPayload.value;
  return payload.updatedAt || '市场快讯与持仓影响';
});

onShow(() => {
  loadTheme();
  restoreSession();
  loadData(false).catch((error) => console.warn('[news:load]', error));
});

onPullDownRefresh(async () => {
  try {
    await loadData(true);
  } catch (error) {
    console.warn('[news:pull-down-refresh]', error);
    uni.showToast({ title: '刷新失败，请稍后重试', icon: 'none' });
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadData(force: boolean) {
  if (loading.value) return;
  const hasPageData =
    (Array.isArray(globalPayload.value.items) && globalPayload.value.items.length > 0) ||
    (Array.isArray(holdingPayload.value.items) && holdingPayload.value.items.length > 0);
  if (!force && hasPageData && Date.now() - loadedAt.value < PAGE_CACHE_TTL) return;

  loading.value = true;
  try {
    const [globalResult, holdingResult] = await Promise.allSettled([
      getGlobalNews(force, false, 60),
      sessionState.username ? getHoldingNews(sessionState.username, force, false, 40) : Promise.resolve({ items: [] } as NewsResponse)
    ]);

    if (globalResult.status === 'fulfilled') {
      globalPayload.value = globalResult.value || {};
    } else {
      console.warn('[news:global]', globalResult.reason);
    }

    if (holdingResult.status === 'fulfilled') {
      holdingPayload.value = holdingResult.value || {};
    } else {
      console.warn('[news:holding]', holdingResult.reason);
    }

    loadedAt.value = Date.now();
  } finally {
    loading.value = false;
  }
}

function newsItemKey(item: NewsItem, index: number) {
  return `${item.id || item.title || item.showTime || 'news'}-${index}`;
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';
@import '../../styles/mixins.scss';

.news-page {
  display: flex;
  flex-direction: column;
  gap: 24rpx;
  padding-top: 34rpx;
}

.switch-card {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12rpx;
  padding: 12rpx;
  border-radius: 999rpx;
}

.switch-btn {
  height: 72rpx;
  padding: 0;
  border-radius: 999rpx;
  color: var(--text-muted);
  background: transparent;
  font-size: 25rpx;
  font-weight: 900;
}

.switch-btn.active {
  @include primary-gradient;
  color: var(--button-primary-text);
}

.notice-card {
  padding: 22rpx 26rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  line-height: 1.55;
}

.news-card {
  padding: 28rpx;
  background:
    radial-gradient(circle at 12% 0%, rgba(255, 95, 162, 0.1), transparent 30%),
    radial-gradient(circle at 90% 8%, rgba(56, 189, 248, 0.08), transparent 28%),
    rgba(18, 28, 56, 0.72);
}

.news-head {
  display: flex;
  gap: 20rpx;
}

.time-dot {
  width: 18rpx;
  height: 18rpx;
  margin-top: 12rpx;
  border-radius: 50%;
  background: $rainbow-gradient;
  box-shadow: 0 0 24rpx rgba(139, 92, 246, 0.45);
}

.news-main {
  min-width: 0;
  flex: 1;
}

.news-meta {
  display: flex;
  justify-content: space-between;
  gap: 18rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.news-title {
  display: block;
  margin-top: 12rpx;
  color: var(--text-primary);
  font-size: 30rpx;
  font-weight: 900;
  line-height: 1.42;
}

.news-summary {
  display: block;
  margin-top: 12rpx;
  color: var(--text-secondary);
  font-size: 24rpx;
  line-height: 1.5;
}

.tag-row {
  display: flex;
  flex-wrap: wrap;
  gap: 10rpx;
  margin-top: 18rpx;
}

.tag {
  padding: 7rpx 14rpx;
  border-radius: 999rpx;
  color: #dbeafe;
  background: linear-gradient(135deg, rgba(255, 95, 162, 0.1), rgba(139, 92, 246, 0.12), rgba(56, 189, 248, 0.1));
  font-size: 20rpx;
  font-weight: 800;
}

.tag.important {
  color: #fecaca;
  background: rgba(255, 77, 79, 0.16);
}
</style>
