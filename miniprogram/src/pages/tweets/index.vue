<template>
  <view :class="['page-shell', 'tweets-page', themeClass]">
    <view class="page-header">
      <view>
        <text class="page-title">白毛股神推文</text>
        <text class="page-subtitle">@aleabitoreddit · 仅供个人观察</text>
        <text v-if="payload.fetchedAt" class="page-subtitle">缓存时间 {{ formatTime(payload.fetchedAt) }}</text>
      </view>
      <button class="refresh-button" :disabled="loading" @tap="loadData(true)">{{ loading ? '同步中' : '刷新' }}</button>
    </view>

    <view v-if="loading && posts.length === 0" class="glass-card empty-card">正在读取推文缓存...</view>
    <view v-else-if="payload.status === 'unavailable' || payload.status === 'invalid'" class="glass-card empty-card" @tap="loadData(true)">暂时无法获取推文，点击重试</view>
    <view v-else-if="posts.length === 0" class="glass-card empty-card" @tap="loadData(true)">暂无推文缓存，点击刷新</view>

    <view v-for="post in posts" :key="post.externalId || post.id" class="glass-card tweet-card">
      <view class="tweet-meta">
        <text>{{ formatTime(post.createdAt) }}</text>
        <text>{{ post.authorName || 'Serenity' }}</text>
      </view>
      <text v-if="post.translatedText" class="translated-text">{{ post.translatedText }}</text>
      <text class="original-text">{{ post.text }}</text>
      <text v-if="post.translationStatus === 'failed'" class="translation-status">翻译失败</text>
      <text v-else-if="post.translationStatus === 'skipped' && !post.translatedText" class="translation-status">未配置翻译</text>
      <text v-else-if="!post.translatedText" class="translation-status">待翻译</text>
      <view class="tweet-footer">
        <view class="tweet-stats">
          <text>赞 {{ formatCount(post.likeCount) }}</text>
          <text>转 {{ formatCount(post.retweetCount) }}</text>
          <text>回 {{ formatCount(post.replyCount) }}</text>
        </view>
        <button class="original-link" @tap="openOriginal(post.url)">复制链接</button>
      </view>

      <!-- 评论/回复区域 -->
      <view v-if="post.replies && post.replies.length" class="replies-section">
        <button class="replies-toggle" @tap="toggleReplies(post)">
          {{ post._showReplies ? '收起评论' : `展开评论 (${post.replies.length})` }}
        </button>
        <view v-if="post._showReplies" class="replies-list">
          <view v-for="reply in post.replies" :key="reply.id || reply.createdAt" class="reply-card">
            <view class="reply-meta">
              <text>{{ reply.authorName || reply.authorUsername || '回复者' }}</text>
              <text>{{ formatTime(reply.createdAt) }}</text>
            </view>
            <text v-if="reply.translatedText" class="reply-translation">{{ reply.translatedText }}</text>
            <text class="reply-original">{{ reply.text }}</text>
            <text v-if="reply.translationStatus === 'failed'" class="translation-status">翻译失败</text>
            <text v-else-if="reply.translationStatus === 'skipped' && !reply.translatedText" class="translation-status">未配置翻译</text>
            <text v-else-if="!reply.translatedText" class="translation-status">待翻译</text>
          </view>
        </view>
      </view>
    </view>

    <view class="safe-tabbar-space" />
    <AppTabBar active="tweets" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import AppTabBar from '../../components/AppTabBar.vue';
import { getInfluencerPosts, type InfluencerPostsResponse } from '../../services/api/influencer';
import { loadTheme, themeClass } from '../../stores/theme';

const loading = ref(false);
const payload = ref<InfluencerPostsResponse>({ status: 'idle', items: [] });

const posts = computed(() => (Array.isArray(payload.value.items) ? payload.value.items : [])
  .slice()
  .sort((left, right) => Date.parse(right.createdAt || '') - Date.parse(left.createdAt || ''))
  .slice(0, 20));

onShow(() => {
  loadTheme();
  loadData(false).catch((error) => console.warn('[tweets:load]', error));
});

onPullDownRefresh(async () => {
  try {
    await loadData(true);
  } finally {
    uni.stopPullDownRefresh();
  }
});

async function loadData(force: boolean) {
  if (loading.value) return;
  loading.value = true;
  try {
    payload.value = await getInfluencerPosts(force);
  } finally {
    loading.value = false;
  }
}

function formatTime(value?: string) {
  const date = new Date(value || '');
  if (Number.isNaN(date.getTime())) return '--';
  return date.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false
  });
}

function formatCount(value?: number) {
  const count = Number(value || 0);
  if (!Number.isFinite(count) || count <= 0) return '0';
  if (count >= 10000) return `${(count / 10000).toFixed(count >= 100000 ? 0 : 1)}万`;
  return String(Math.round(count));
}

function openOriginal(url: string) {
  if (!url) return;
  uni.setClipboardData({
    data: url,
    success: () => { uni.showToast({ title: '原文链接已复制', icon: 'none', duration: 1500 }); },
    fail: () => { uni.showToast({ title: '复制失败', icon: 'none', duration: 1500 }); }
  });
}

function toggleReplies(post: any) {
  post._showReplies = !post._showReplies;
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';
@import '../../styles/mixins.scss';

.tweets-page {
  display: flex;
  flex-direction: column;
  gap: 24rpx;
  padding-top: 34rpx;
}

.refresh-button,
.original-link {
  min-width: 112rpx;
  height: 64rpx;
  padding: 0 22rpx;
  border-radius: 999rpx;
  color: var(--text-secondary);
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
  font-size: 22rpx;
  font-weight: 900;
}

.refresh-button::after,
.original-link::after {
  border: none;
}

.refresh-button[disabled] {
  opacity: 0.55;
}

.tweet-card {
  padding: 28rpx;
}

.tweet-meta,
.tweet-footer,
.tweet-stats {
  display: flex;
  align-items: center;
}

.tweet-meta,
.tweet-footer {
  justify-content: space-between;
  gap: 18rpx;
}

.tweet-meta,
.tweet-stats {
  color: var(--text-muted);
  font-size: 22rpx;
  font-weight: 800;
}

.translated-text,
.original-text {
  display: block;
  white-space: pre-wrap;
  word-break: break-word;
}

.translated-text {
  margin-top: 18rpx;
  color: var(--text-primary);
  font-size: 29rpx;
  line-height: 1.65;
}

.original-text {
  margin-top: 16rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  line-height: 1.55;
}

.translation-status {
  display: inline-block;
  margin-top: 14rpx;
  padding: 6rpx 14rpx;
  border-radius: 999rpx;
  color: #fbbf24;
  background: rgba(251, 191, 36, 0.12);
  font-size: 20rpx;
  font-weight: 900;
}

.tweet-footer {
  margin-top: 20rpx;
}

.tweet-stats {
  flex-wrap: wrap;
  gap: 16rpx;
}

.original-link {
  color: #60a5fa;
}

.replies-section { margin-top: 12px; border-top: 2rpx solid rgba(128,128,128,.15); padding-top: 12px; }
.replies-toggle { background: none; border: 2rpx solid rgba(128,128,128,.2); color: var(--text-muted); border-radius: 999px; padding: 6rpx 16rpx; font-size: 22rpx; font-weight: 800; }
.replies-list { margin-top: 10px; display: flex; flex-direction: column; gap: 8px; }
.reply-card { background: rgba(128,128,128,.05); border-radius: 12rpx; padding: 12rpx 16rpx; }
.reply-meta { display: flex; justify-content: space-between; color: var(--text-muted); font-size: 22rpx; font-weight: 800; margin-bottom: 6rpx; }
.reply-translation { color: var(--text-main); font-size: 26rpx; line-height: 1.6; }
.reply-original { color: var(--text-muted); font-size: 22rpx; line-height: 1.5; margin-top: 6rpx; white-space: pre-wrap; word-break: break-all; }
</style>
