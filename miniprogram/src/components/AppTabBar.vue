<template>
  <view :class="['app-tabbar', themeClass]">
    <button
      v-for="item in tabs"
      :key="item.key"
      :class="['tab-item', active === item.key ? 'active' : '']"
      @tap="handleTap(item.key)"
    >
      <text class="tab-icon">{{ item.icon }}</text>
      <text class="tab-label">{{ item.label }}</text>
    </button>
  </view>
</template>

<script setup lang="ts">
import { themeState, themeClass } from '../stores/theme';

type TabKey = 'home' | 'sector' | 'news' | 'analysis' | 'tweets';

defineProps<{
  active: TabKey;
}>();

const tabs: Array<{ key: TabKey; icon: string; label: string }> = [
  { key: 'home', icon: '💼', label: '持仓' },
  { key: 'sector', icon: '🔥', label: '板块' },
  { key: 'news', icon: '📡', label: '资讯' },
  { key: 'analysis', icon: '📊', label: '盈亏' },
  { key: 'tweets', icon: '🐦', label: '推文' }
];

function handleTap(key: TabKey) {
  const routes: Record<TabKey, string> = {
    home: '/pages/home/index',
    sector: '/pages/sector/index',
    news: '/pages/news/index',
    analysis: '/pages/analysis/index',
    tweets: '/pages/tweets/index'
  };
  uni.reLaunch({ url: routes[key] });
}
</script>

<style lang="scss" scoped>
@import '../styles/variables.scss';
@import '../styles/mixins.scss';

.app-tabbar {
  @include tabbar-shell;
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 10rpx;
  backdrop-filter: blur(24rpx);
  -webkit-backdrop-filter: blur(24rpx);
  background: var(--tab-bg);
  border-color: var(--border-color);
  box-shadow: var(--card-shadow);
}

.tab-item {
  min-width: 0;
  height: 124rpx;
  padding: 0 8rpx;
  border-radius: 62rpx;
  color: var(--text-muted);
  background: rgba(243, 244, 248, 0.7);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8rpx;
  line-height: 1;
}

.tab-item::after {
  border: none;
}

.tab-item.active {
  background: var(--tab-active-bg);
  color: #fff;
  box-shadow: $shadow-glow;
}

.tab-icon {
  font-size: 43rpx;
  line-height: 44rpx;
}

.tab-label {
  font-size: 27rpx;
  font-weight: 900;
}
</style>
