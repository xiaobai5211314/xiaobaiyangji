<template>
  <view class="page-shell profile-page">
    <view class="page-header">
      <view>
        <text class="page-title">个人中心</text>
        <text class="page-subtitle">头像与登录状态</text>
      </view>
      <button class="back-button" @tap="goBack">返回</button>
    </view>

    <view class="glass-card profile-card">
      <button class="profile-avatar" :disabled="avatarUploading" @tap="changeAvatar">
        <image v-if="avatarUrl" class="avatar-img" :src="avatarUrl" mode="aspectFill" />
        <view v-else class="avatar-fallback">
          <text>{{ avatarText }}</text>
        </view>
      </button>
      <text class="profile-name">{{ displayUsername }}</text>
      <text class="profile-subtitle">点击头像或按钮可更换头像</text>

      <button class="primary-gradient-button action-button" :disabled="avatarUploading" @tap="changeAvatar">
        {{ avatarUploading ? '上传中...' : '更换头像' }}
      </button>
      <button class="logout-button" @tap="logout">退出登录</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onShow } from '@dcloudio/uni-app';
import { uploadAvatar } from '../../services/api/auth';
import { clearSession, loadSession, saveSession, sessionState } from '../../stores/session';
import { avatarInitial } from '../../utils/format';

const avatarUploading = ref(false);
const avatarUrl = computed(() => sessionState.avatarDataUrl || sessionState.avatarUrl || '');
const avatarText = computed(() => avatarInitial(sessionState.username));
const displayUsername = computed(() => sessionState.displayName || sessionState.username || '未登录');

onShow(() => {
  loadSession();
  if (!sessionState.username) {
    uni.reLaunch({ url: '/pages/login/index' });
  }
});

function chooseImage() {
  return new Promise<string | null>((resolve, reject) => {
    uni.chooseImage({
      count: 1,
      sourceType: ['album', 'camera'],
      success: (result) => resolve(result.tempFilePaths?.[0] || null),
      fail: (error) => {
        const message = String((error as { errMsg?: unknown })?.errMsg || '');
        if (/cancel/i.test(message)) {
          resolve(null);
          return;
        }
        reject(error);
      }
    });
  });
}

async function changeAvatar() {
  if (!sessionState.username || avatarUploading.value) return;

  try {
    const filePath = await chooseImage();
    if (!filePath) return;

    avatarUploading.value = true;
    uni.showLoading({ title: '上传头像', mask: true });
    const result = await uploadAvatar(sessionState.username, filePath);
    const avatar = result.avatarDataUrl || result.avatarUrl || '';
    if (!avatar) throw new Error('头像上传成功但未返回头像数据');

    saveSession({
      username: sessionState.username,
      displayName: sessionState.displayName || sessionState.username,
      avatarDataUrl: result.avatarDataUrl || avatar,
      avatarUrl: result.avatarUrl || '',
      loginTime: sessionState.loginTime || Date.now()
    });
    uni.showToast({ title: '头像已更新', icon: 'none' });
  } catch (error) {
    console.warn('[profile:avatar-upload]', error);
    uni.showToast({ title: getErrorMessage(error, '头像上传失败'), icon: 'none' });
  } finally {
    avatarUploading.value = false;
    uni.hideLoading();
  }
}

function logout() {
  clearSession();
  uni.reLaunch({ url: '/pages/login/index' });
}

function goBack() {
  uni.navigateBack({
    fail: () => uni.reLaunch({ url: '/pages/home/index' })
  });
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message) return error.message;
  if (error && typeof error === 'object' && 'errMsg' in error) {
    return String((error as { errMsg?: unknown }).errMsg || fallback);
  }
  return fallback;
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';

.profile-page {
  display: flex;
  flex-direction: column;
  gap: 30rpx;
  padding-top: 34rpx;
}

.back-button,
.logout-button {
  border-radius: 999rpx;
  color: $text-soft;
  background: rgba(148, 163, 184, 0.12);
  border: 1rpx solid rgba(148, 163, 184, 0.16);
  font-size: 24rpx;
  font-weight: 900;
}

.back-button {
  min-width: 104rpx;
  height: 56rpx;
  line-height: 56rpx;
}

.profile-card {
  padding: 42rpx 34rpx;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 22rpx;
  background:
    radial-gradient(circle at 50% 8%, rgba(96, 165, 250, 0.22), transparent 34%),
    linear-gradient(135deg, rgba(30, 41, 59, 0.72), rgba(30, 27, 75, 0.58));
}

.profile-avatar {
  width: 148rpx;
  height: 148rpx;
  padding: 0;
  border-radius: 50%;
  background: transparent;
}

.avatar-img,
.avatar-fallback {
  width: 148rpx;
  height: 148rpx;
  border-radius: 50%;
  border: 2rpx solid rgba(255, 255, 255, 0.24);
  box-shadow: 0 20rpx 48rpx rgba(59, 130, 246, 0.28), 0 0 28rpx rgba(139, 92, 246, 0.18);
}

.avatar-fallback {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 58rpx;
  font-weight: 900;
  background: linear-gradient(135deg, $primary-blue, $primary-purple);
}

.profile-name {
  max-width: 100%;
  color: $text-white;
  font-size: 38rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.profile-subtitle {
  color: $text-muted;
  font-size: 24rpx;
}

.action-button,
.logout-button {
  width: 100%;
  min-height: 88rpx;
}

.logout-button {
  color: #fecaca;
  background: rgba(239, 68, 68, 0.12);
  border-color: rgba(239, 68, 68, 0.22);
}
</style>
