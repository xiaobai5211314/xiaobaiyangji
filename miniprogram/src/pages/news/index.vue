<template>
  <view class="page-shell news-page">
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
      <text>数据仅供个人记录与行情参考，不构成投资建议，实际数据以基金公司、交易所或券商披露为准。</text>
    </view>

    <view v-if="activeItems.length === 0 && !loading" class="glass-card empty-card" @tap="loadData(true)">
      <text>{{ mode === 'holding' && !sessionState.username ? '登录后可同步你的个人持仓记录。' : '暂无资讯数据，点击重试或下拉刷新' }}</text>
    </view>

    <view v-for="item in activeItems" :key="item.id || item.title" class="glass-card news-card">
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
            <text v-for="tag in item.tags || []" :key="tag" class="tag">{{ tag }}</text>
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

const loading = ref(false);
const mode = ref<'global' | 'holding'>('global');
const globalPayload = ref<NewsResponse>({});
const holdingPayload = ref<NewsResponse>({});

const activeItems = computed<NewsItem[]>(() => {
  const rows = mode.value === 'holding' ? holdingPayload.value.items : globalPayload.value.items;
  return Array.isArray(rows) ? rows : [];
});
const updatedAtText = computed(() => {
  const payload = mode.value === 'holding' ? holdingPayload.value : globalPayload.value;
  return payload.updatedAt || '市场快讯与持仓影响';
});

onShow(() => {
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
  loading.value = true;
  try {
    const globalTask = getGlobalNews(force, false, 60).catch((error) => {
      console.warn('[news:global]', error);
      return { items: [] } as NewsResponse;
    });
    const holdingTask = sessionState.username
      ? getHoldingNews(sessionState.username, force, false, 40).catch((error) => {
          console.warn('[news:holding]', error);
          return { items: [] } as NewsResponse;
        })
      : Promise.resolve({ items: [] } as NewsResponse);

    const [globalNews, holdingNews] = await Promise.all([globalTask, holdingTask]);
    globalPayload.value = globalNews || {};
    holdingPayload.value = holdingNews || {};
  } finally {
    loading.value = false;
  }
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
  color: $text-muted;
  background: transparent;
  font-size: 25rpx;
  font-weight: 900;
}

.switch-btn.active {
  @include primary-gradient;
  color: #fff;
}

.notice-card {
  padding: 22rpx 26rpx;
  color: $text-muted;
  font-size: 22rpx;
  line-height: 1.55;
}

.news-card {
  padding: 28rpx;
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
  background: $primary-blue;
  box-shadow: 0 0 24rpx rgba(59, 130, 246, 0.65);
}

.news-main {
  min-width: 0;
  flex: 1;
}

.news-meta {
  display: flex;
  justify-content: space-between;
  gap: 18rpx;
  color: $text-muted;
  font-size: 22rpx;
}

.news-title {
  display: block;
  margin-top: 12rpx;
  color: $text-white;
  font-size: 30rpx;
  font-weight: 900;
  line-height: 1.42;
}

.news-summary {
  display: block;
  margin-top: 12rpx;
  color: $text-soft;
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
  background: rgba(59, 130, 246, 0.14);
  font-size: 20rpx;
  font-weight: 800;
}

.tag.important {
  color: #fecaca;
  background: rgba(255, 77, 79, 0.16);
}
</style>
