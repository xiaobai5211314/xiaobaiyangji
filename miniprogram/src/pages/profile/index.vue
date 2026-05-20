<template>
  <view :class="['page-shell', 'profile-page', themeClass]">
    <view class="page-header">
      <view>
        <text class="page-title">个人中心</text>
        <text class="page-subtitle">个人资料与主题设置</text>
      </view>
      <button class="back-button" @tap="goBack">返回</button>
    </view>

    <view class="glass-card profile-card">
      <button
        v-if="sessionState.username"
        class="profile-avatar"
        open-type="chooseAvatar"
        :disabled="profileSaving"
        @chooseavatar="onChooseAvatar"
      >
        <image v-if="avatarUrl" class="avatar-img" :src="avatarUrl" mode="aspectFill" />
        <view v-else class="avatar-fallback">
          <text>{{ avatarText }}</text>
        </view>
      </button>
      <button v-else class="profile-avatar" @tap="navigateToLogin">
        <view class="avatar-fallback">
          <text>{{ avatarText }}</text>
        </view>
      </button>
      <text class="profile-name">{{ displayUsername }}</text>
      <text class="profile-subtitle">{{ profileSubtitle }}</text>

      <view v-if="sessionState.username" class="profile-form">
        <view class="profile-field">
          <text class="field-label">昵称</text>
          <input
            class="nickname-input"
            type="nickname"
            :value="nicknameDraft"
            placeholder="点击填写或选择微信昵称"
            placeholder-class="input-placeholder"
            @input="onNicknameInput"
          />
        </view>

        <button class="primary-gradient-button action-button" :disabled="profileSaving" @tap="saveProfile">
          {{ profileSaving ? '保存中...' : '保存资料' }}
        </button>

        <button class="secondary-action action-button" :disabled="profileSaving" @tap="changeAvatar">
          从相册上传头像
        </button>
      </view>

      <button v-else class="primary-gradient-button action-button" @tap="navigateToLogin">
        登录 / 同步持仓
      </button>

      <view class="theme-panel">
        <view class="theme-head">
          <text class="theme-title">主题切换</text>
          <text class="theme-subtitle">仅调整视觉，不重新请求数据</text>
        </view>
        <view class="theme-options">
          <button
            v-for="item in themeOptions"
            :key="item.value"
            :class="['theme-option', themeState.theme === item.value ? 'active' : '']"
            @tap="selectTheme(item.value)"
          >
            <text>{{ item.label }}</text>
            <text>{{ item.description }}</text>
          </button>
        </view>
      </view>

      <button v-if="sessionState.username" class="logout-button" @tap="logout">退出登录</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onShow } from '@dcloudio/uni-app';
import { isGeneratedWechatUsername, pickAvatar, pickDisplayName, updateProfile, uploadAvatar } from '../../services/api/auth';
import { clearSession, loadSession, saveSession, sessionState } from '../../stores/session';
import { loadTheme, setTheme, themeClass, themeOptions, themeState, type AppTheme } from '../../stores/theme';
import { avatarInitial } from '../../utils/format';

interface ChooseAvatarEvent {
  detail?: {
    avatarUrl?: string;
  };
}

const profileSaving = ref(false);
const nicknameDraft = ref('');
const selectedAvatarPath = ref('');
const previewAvatarUrl = ref('');
const avatarUrl = computed(() => previewAvatarUrl.value || sessionState.avatarDataUrl || sessionState.avatarUrl || '');
const displayUsername = computed(() => {
  const nickname = String(sessionState.displayName || '').trim();
  if (nickname) return nickname;
  if (sessionState.username && isGeneratedWechatUsername(sessionState.username)) return '未设置昵称';
  return sessionState.username || '未登录';
});
const avatarText = computed(() => avatarInitial(displayUsername.value === '未设置昵称' ? '估' : displayUsername.value));
const profileSubtitle = computed(() =>
  sessionState.username ? '可选择微信头像、填写昵称后保存' : '登录后可同步你的个人持仓记录。'
);

onShow(() => {
  loadTheme();
  loadSession();
  syncDraftFromSession();
});

function syncDraftFromSession() {
  nicknameDraft.value = sessionState.displayName && !isGeneratedWechatUsername(sessionState.displayName)
    ? sessionState.displayName
    : '';
  selectedAvatarPath.value = '';
  previewAvatarUrl.value = '';
}

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
  if (!sessionState.username) {
    uni.showToast({ title: '登录后可使用该功能', icon: 'none' });
    return;
  }
  if (profileSaving.value) return;

  try {
    const filePath = await chooseImage();
    if (!filePath) return;

    selectedAvatarPath.value = filePath;
    previewAvatarUrl.value = filePath;
    uni.showToast({ title: '头像已选择，请保存资料', icon: 'none' });
  } catch (error) {
    console.warn('[profile:choose-avatar]', error);
    uni.showToast({ title: getErrorMessage(error, '头像选择失败'), icon: 'none' });
  }
}

function onChooseAvatar(event: ChooseAvatarEvent) {
  if (!sessionState.username) {
    navigateToLogin();
    return;
  }

  const avatarUrl = String(event.detail?.avatarUrl || '').trim();
  if (!avatarUrl) {
    uni.showToast({ title: '未选择头像', icon: 'none' });
    return;
  }

  selectedAvatarPath.value = avatarUrl;
  previewAvatarUrl.value = avatarUrl;
}

function onNicknameInput(event: Event) {
  const detail = (event as unknown as { detail?: { value?: string } }).detail;
  nicknameDraft.value = String(detail?.value || '');
}

async function saveProfile() {
  if (!sessionState.username) {
    uni.showToast({ title: '登录后可使用该功能', icon: 'none' });
    return;
  }
  if (profileSaving.value) return;

  profileSaving.value = true;
  uni.showLoading({ title: '保存资料', mask: true });

  try {
    let avatarDataUrl = sessionState.avatarDataUrl || '';
    let avatarUrl = sessionState.avatarUrl || '';

    if (selectedAvatarPath.value) {
      const uploadResult = await uploadAvatar(sessionState.username, selectedAvatarPath.value);
      const uploadedAvatar = uploadResult.avatarDataUrl || uploadResult.avatarUrl || '';
      if (!uploadedAvatar) throw new Error('头像上传成功但未返回头像数据');

      avatarDataUrl = uploadResult.avatarDataUrl || uploadedAvatar;
      avatarUrl = uploadResult.avatarUrl || '';
    }

    const result = await updateProfile({
      username: sessionState.username,
      displayName: nicknameDraft.value.trim(),
      avatarDataUrl: selectedAvatarPath.value ? avatarDataUrl : undefined
    });

    const nextAvatar = pickAvatar(result) || avatarDataUrl || avatarUrl;
    const nextDisplayName = pickDisplayName(result, sessionState.username) || nicknameDraft.value.trim();
    saveSession({
      username: sessionState.username,
      displayName: nextDisplayName,
      avatarDataUrl: nextAvatar,
      avatarUrl,
      loginTime: sessionState.loginTime || Date.now()
    });

    selectedAvatarPath.value = '';
    previewAvatarUrl.value = '';
    nicknameDraft.value = nextDisplayName;
    uni.showToast({ title: '资料已保存', icon: 'none' });
  } catch (error) {
    console.warn('[profile:save]', error);
    uni.showToast({ title: getErrorMessage(error, '资料保存失败'), icon: 'none' });
  } finally {
    profileSaving.value = false;
    uni.hideLoading();
  }
}

function navigateToLogin() {
  uni.navigateTo({
    url: '/pages/login/index',
    fail: () => uni.redirectTo({ url: '/pages/login/index' })
  });
}

function logout() {
  clearSession();
  uni.reLaunch({ url: '/pages/login/index' });
}

function selectTheme(theme: AppTheme) {
  setTheme(theme);
  uni.showToast({
    title: themeOptions.find((item) => item.value === theme)?.label || '主题已切换',
    icon: 'none'
  });
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
  color: var(--text-secondary);
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
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
  background: var(--card-bg);
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
  border: 2rpx solid var(--border-color);
  box-shadow: 0 20rpx 48rpx rgba(59, 130, 246, 0.28), 0 0 28rpx rgba(139, 92, 246, 0.18);
}

.avatar-fallback {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--button-primary-text);
  font-size: 58rpx;
  font-weight: 900;
  background: linear-gradient(135deg, $primary-blue, $primary-purple);
}

.profile-name {
  max-width: 100%;
  color: var(--text-primary);
  font-size: 38rpx;
  font-weight: 900;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.profile-subtitle {
  color: var(--text-muted);
  font-size: 24rpx;
}

.action-button,
.logout-button {
  width: 100%;
  min-height: 88rpx;
}

.profile-form {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 18rpx;
}

.profile-field {
  width: 100%;
  box-sizing: border-box;
  padding: 22rpx 24rpx;
  border-radius: 30rpx;
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
}

.field-label {
  display: block;
  margin-bottom: 12rpx;
  color: var(--text-muted);
  font-size: 22rpx;
  font-weight: 800;
}

.nickname-input {
  width: 100%;
  min-height: 58rpx;
  color: var(--text-primary);
  font-size: 30rpx;
  font-weight: 900;
}

.input-placeholder {
  color: var(--text-muted);
  font-weight: 500;
}

.secondary-action {
  color: var(--text-secondary);
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
  border-radius: 999rpx;
  font-size: 26rpx;
  font-weight: 900;
}

.theme-panel {
  width: 100%;
  box-sizing: border-box;
  padding: 24rpx;
  border-radius: 32rpx;
  background: var(--panel-soft);
  border: 1rpx solid var(--border-color);
}

.theme-head {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  margin-bottom: 18rpx;
}

.theme-title {
  color: var(--text-primary);
  font-size: 28rpx;
  font-weight: 900;
}

.theme-subtitle {
  color: var(--text-muted);
  font-size: 22rpx;
}

.theme-options {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16rpx;
}

.theme-option {
  min-height: 112rpx;
  padding: 18rpx;
  border-radius: 30rpx;
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
  color: var(--text-secondary);
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 8rpx;
  text-align: left;
}

.theme-option::after {
  border: none;
}

.theme-option text:first-child {
  font-size: 26rpx;
  font-weight: 900;
}

.theme-option text:last-child {
  color: var(--text-muted);
  font-size: 21rpx;
}

.theme-option.active {
  color: var(--button-primary-text);
  background: var(--button-primary-bg);
  border-color: var(--border-color);
  box-shadow: 0 16rpx 34rpx rgba(139, 92, 246, 0.18);
}

.theme-option.active text:last-child {
  color: rgba(255, 255, 255, 0.84);
}

.logout-button {
  color: var(--profit-color);
  background: rgba(239, 68, 68, 0.12);
  border-color: rgba(239, 68, 68, 0.22);
}
</style>
