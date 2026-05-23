<template>
  <view :class="['page-shell', 'login-page', themeClass]">
    <view class="brand">
      <view class="brand-mark">
        <text>养</text>
      </view>
      <text class="brand-title">小白养基</text>
      <text class="brand-subtitle">看清持仓与收益节奏</text>
    </view>

    <view class="glass-card login-card">
      <view class="card-head">
        <text class="section-title">登录与同步</text>
        <text class="muted-text">微信登录或继续使用账号密码</text>
      </view>

      <button class="wechat-action" :disabled="wechatSubmitting || submitting || registering" @tap="wechatOneTapLogin">
        {{ wechatSubmitting ? '微信登录中...' : '微信一键登录' }}
      </button>

      <view class="login-divider">
        <view class="divider-line"></view>
        <text>账号密码登录/注册</text>
        <view class="divider-line"></view>
      </view>

      <view class="field">
        <text class="label">账号</text>
        <input
          v-model.trim="form.username"
          class="input"
          placeholder="输入用户名"
          placeholder-class="input-placeholder"
          confirm-type="next"
        />
      </view>

      <view class="field">
        <text class="label">密码</text>
        <input
          v-model="form.password"
          class="input"
          password
          placeholder="输入密码"
          placeholder-class="input-placeholder"
          confirm-type="done"
          @confirm="submit"
        />
      </view>

      <view v-if="errorMessage" class="error-banner">
        <text>{{ errorMessage }}</text>
      </view>

      <button class="primary-gradient-button action-button" :disabled="submitting" @tap="submit">
        {{ submitting ? '登录中...' : '登录' }}
      </button>

      <button class="secondary-action" :disabled="registering || submitting" @tap="registerAccount">
        {{ registering ? '注册中...' : '注册账号' }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { onShow } from '@dcloudio/uni-app';
import { login, pickAvatar, pickDisplayName, pickUsername, register, wechatLogin } from '../../services/api/auth';
import { loadSession, saveSession } from '../../stores/session';
import { loadTheme, themeClass } from '../../stores/theme';

const submitting = ref(false);
const registering = ref(false);
const wechatSubmitting = ref(false);
const errorMessage = ref('');
const form = reactive({
  username: '',
  password: ''
});

onShow(() => {
  loadTheme();
  const session = loadSession();
  if (session?.username) {
    uni.reLaunch({ url: '/pages/home/index' });
  }
});

function getErrorMessage(error: unknown) {
  if (error instanceof Error && error.message) return error.message;
  if (error && typeof error === 'object' && 'errMsg' in error) {
    return String((error as { errMsg?: unknown }).errMsg || '');
  }
  return '操作失败，请稍后重试';
}

function getWechatErrorMessage(error: unknown) {
  const message = getErrorMessage(error);
  if (message.includes('未获取到微信登录凭证') || message.includes('login:fail')) {
    return '可继续使用账号密码登录';
  }

  return message;
}

function readCredentials() {
  const username = form.username.trim();
  const password = form.password;

  if (!username || !password) {
    errorMessage.value = '请输入账号和密码';
    uni.showToast({ title: '请输入账号和密码', icon: 'none' });
    return null;
  }

  return { username, password };
}

function getWechatLoginCode() {
  return new Promise<string>((resolve, reject) => {
    uni.login({
      provider: 'weixin',
      success: (result) => {
        const code = (result.code || '').trim();
        if (!code) {
          reject(new Error('未获取到微信登录凭证'));
          return;
        }

        resolve(code);
      },
      fail: reject
    });
  });
}

async function wechatOneTapLogin() {
  if (wechatSubmitting.value) return;

  wechatSubmitting.value = true;
  errorMessage.value = '';

  try {
    const code = await getWechatLoginCode();
    const result = await wechatLogin({ code });
    const username = pickUsername(result);
    if (!username) throw new Error('微信登录成功但未返回账号');

    saveSession({
      username,
      displayName: pickDisplayName(result, username),
      avatarDataUrl: pickAvatar(result),
      loginTime: Date.now()
    });

    form.password = '';
    uni.reLaunch({ url: '/pages/home/index' });
  } catch (error) {
    console.warn('[login:wechat]', error);
    const message = getWechatErrorMessage(error);
    errorMessage.value = message;
    uni.showToast({ title: message, icon: 'none' });
  } finally {
    wechatSubmitting.value = false;
  }
}

async function submit() {
  const credentials = readCredentials();
  if (!credentials) return;

  submitting.value = true;
  errorMessage.value = '';
  try {
    const result = await login(credentials);
    const username = pickUsername(result, credentials.username);
    saveSession({
      username,
      displayName: pickDisplayName(result, username),
      avatarDataUrl: pickAvatar(result),
      loginTime: Date.now()
    });
    form.password = '';
    uni.reLaunch({ url: '/pages/home/index' });
  } catch (error) {
    console.warn('[login:submit]', error);
    errorMessage.value = getErrorMessage(error);
  } finally {
    submitting.value = false;
  }
}

async function registerAccount() {
  const credentials = readCredentials();
  if (!credentials) return;

  registering.value = true;
  errorMessage.value = '';
  try {
    await register(credentials);
    form.password = '';
    uni.showToast({ title: '注册成功，请登录', icon: 'none' });
  } catch (error) {
    console.warn('[login:register]', error);
    errorMessage.value = getErrorMessage(error);
  } finally {
    registering.value = false;
  }
}
</script>

<style lang="scss" scoped>
@import '../../styles/variables.scss';
@import '../../styles/mixins.scss';

.login-page {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 54rpx;
  padding-top: 76rpx;
  padding-bottom: 76rpx;
}

.brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14rpx;
  text-align: center;
}

.brand-mark {
  width: 104rpx;
  height: 104rpx;
  border-radius: 36rpx;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--button-primary-text);
  font-size: 48rpx;
  font-weight: 900;
  background: var(--button-primary-bg);
  box-shadow: 0 20rpx 48rpx rgba(59, 130, 246, 0.32);
}

.brand-title {
  color: var(--text-primary);
  font-size: 60rpx;
  font-weight: 900;
  letter-spacing: 0;
}

.brand-subtitle {
  color: var(--text-muted);
  font-size: 26rpx;
}

.login-card {
  display: flex;
  flex-direction: column;
  gap: 30rpx;
  padding: 38rpx;
}

.card-head {
  display: flex;
  flex-direction: column;
  gap: 10rpx;
}

.wechat-action {
  width: 100%;
  min-height: 92rpx;
  margin: 0;
  border-radius: $button-radius;
  border: 1rpx solid rgba(28, 196, 125, 0.34);
  color: var(--button-primary-text);
  background: linear-gradient(135deg, #16a34a 0%, #22c55e 46%, #38bdf8 100%);
  box-shadow: 0 18rpx 42rpx rgba(34, 197, 94, 0.22);
  font-size: 28rpx;
  font-weight: 900;
}

.login-divider {
  display: flex;
  align-items: center;
  gap: 18rpx;
  color: var(--text-muted);
  font-size: 22rpx;
}

.divider-line {
  flex: 1;
  height: 1rpx;
  background: var(--border-color);
}

.field {
  display: flex;
  flex-direction: column;
  gap: 12rpx;
}

.label {
  color: var(--text-secondary);
  font-size: 24rpx;
  font-weight: 700;
}

.input {
  height: 92rpx;
  box-sizing: border-box;
  padding: 0 28rpx;
  border: 1rpx solid var(--border-color);
  border-radius: $control-radius;
  color: var(--input-text);
  background: var(--input-bg);
  font-size: 28rpx;
}

.input-placeholder {
  color: var(--input-placeholder);
}

.error-banner {
  padding: 20rpx 24rpx;
  border: 1rpx solid rgba(255, 77, 79, 0.28);
  border-radius: $control-radius;
  color: #fecaca;
  background: rgba(127, 29, 29, 0.28);
  font-size: 24rpx;
}

.action-button {
  width: 100%;
  margin-top: 8rpx;
}

.secondary-action {
  width: 100%;
  min-height: 84rpx;
  color: var(--text-secondary);
  background: var(--control-bg);
  border: 1rpx solid var(--border-color);
  border-radius: $button-radius;
  font-size: 26rpx;
  font-weight: 800;
}

button[disabled] {
  opacity: 0.62;
}
</style>
