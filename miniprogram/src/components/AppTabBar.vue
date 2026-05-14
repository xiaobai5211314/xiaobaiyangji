<template>
  <view class="app-tabbar">
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
type TabKey = 'home' | 'sector' | 'news' | 'analysis';

defineProps<{
  active: TabKey;
}>();

const tabs: Array<{ key: TabKey; icon: string; label: string }> = [
  { key: 'home', icon: '🛡️', label: '持仓' },
  { key: 'sector', icon: '📈', label: '板块' },
  { key: 'news', icon: '📰', label: '资讯' },
  { key: 'analysis', icon: '📊', label: '盈亏' }
];

function handleTap(key: TabKey) {
  const routes: Record<TabKey, string> = {
    home: '/pages/home/index',
    sector: '/pages/sector/index',
    news: '/pages/news/index',
    analysis: '/pages/analysis/index'
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
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10rpx;
  backdrop-filter: blur(20rpx);
}

.tab-item {
  min-width: 0;
  height: 124rpx;
  padding: 0 8rpx;
  border-radius: 62rpx;
  color: $text-muted;
  background: rgba(18, 28, 56, 0.66);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8rpx;
  line-height: 1;
  box-shadow: inset 0 1rpx 0 rgba(255, 255, 255, 0.05);
}

.tab-item::after {
  border: none;
}

.tab-item.active {
  background:
    radial-gradient(circle at 30% 12%, rgba(255, 255, 255, 0.22), transparent 30%),
    $rainbow-gradient;
  color: #fff;
  box-shadow: 0 12rpx 30rpx rgba(139, 92, 246, 0.18), 0 0 22rpx rgba(56, 189, 248, 0.12), inset 0 1rpx 0 rgba(255, 255, 255, 0.18);
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
