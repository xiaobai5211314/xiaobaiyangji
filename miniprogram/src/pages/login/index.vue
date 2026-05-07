<template>
  <view class="page-shell login-page">
    <view class="brand">
      <view class="brand-mark">
        <text>估</text>
      </view>
      <text class="brand-title">估值助手</text>
      <text class="brand-subtitle">看清持仓与收益节奏</text>
    </view>

    <view class="glass-card login-card">
      <view class="card-head">
        <text class="section-title">账号登录</text>
        <text class="muted-text">查看今日持仓与收益概览</text>
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
import { login, pickAvatar, pickDisplayName, pickUsername, register } from '../../services/api/auth';
import { loadSession, saveSession } from '../../stores/session';

const submitting = ref(false);
const registering = ref(false);
const errorMessage = ref('');
const form = reactive({
  username: '',
  password: ''
});

onShow(() => {
  const session = loadSession();
  if (session?.username) {
    uni.reLaunch({ url: '/pages/home/index' });
  }
});

function getErrorMessage(error: unknown) {
  if (error instanceof Error && error.message) return error.message;
  return '操作失败，请稍后重试';
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
    console.error('[login:submit]', error);
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
    console.error('[login:register]', error);
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
  color: #fff;
  font-size: 48rpx;
  font-weight: 900;
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.92), rgba(99, 102, 241, 0.92));
  box-shadow: 0 20rpx 48rpx rgba(59, 130, 246, 0.32);
}

.brand-title {
  color: $text-white;
  font-size: 60rpx;
  font-weight: 900;
  letter-spacing: 0;
}

.brand-subtitle {
  color: $text-muted;
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

.field {
  display: flex;
  flex-direction: column;
  gap: 12rpx;
}

.label {
  color: $text-soft;
  font-size: 24rpx;
  font-weight: 700;
}

.input {
  height: 92rpx;
  box-sizing: border-box;
  padding: 0 28rpx;
  border: 1rpx solid rgba(148, 163, 184, 0.2);
  border-radius: $control-radius;
  color: $text-white;
  background: rgba(15, 23, 42, 0.76);
  font-size: 28rpx;
}

.input-placeholder {
  color: rgba(148, 163, 184, 0.58);
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
  color: $text-soft;
  background: rgba(96, 165, 250, 0.1);
  border: 1rpx solid rgba(96, 165, 250, 0.3);
  border-radius: $button-radius;
  font-size: 26rpx;
  font-weight: 800;
}

button[disabled] {
  opacity: 0.62;
}
</style>
