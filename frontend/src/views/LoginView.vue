<template>
  <div class="login-container">
    <div class="login-card">
      <h2>🔐 小白养基</h2>
      <p class="subtitle">请登录后继续</p>
      <form @submit.prevent="handleLogin">
        <input v-model="username" type="text" placeholder="账号" autocomplete="username" />
        <input v-model="password" type="password" placeholder="密码" autocomplete="current-password" />
        <button type="submit" :disabled="loading">{{ loading ? '登录中...' : '登录' }}</button>
      </form>
      <p v-if="error" class="error">{{ error }}</p>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { login, saveAuth } from '../api/fundApi'

const router = useRouter()
const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

async function handleLogin() {
  if (!username.value || !password.value) {
    error.value = '请输入账号和密码'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const data = await login(username.value, password.value)
    if (data.success) {
      saveAuth(data)
      router.push('/')
    } else {
      error.value = data.message || '登录失败'
    }
  } catch (e) {
    error.value = e.message || '网络错误'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-container {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #0f172a;
}
.login-card {
  background: rgba(30, 41, 59, 0.9);
  border: 1px solid rgba(148, 163, 184, 0.2);
  border-radius: 16px;
  padding: 40px 32px;
  width: 320px;
  text-align: center;
}
.login-card h2 {
  color: #f8fafc;
  margin: 0 0 8px;
  font-size: 22px;
}
.subtitle {
  color: #94a3b8;
  margin: 0 0 24px;
  font-size: 14px;
}
form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
input {
  padding: 12px 16px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.3);
  background: rgba(15, 23, 42, 0.6);
  color: #f8fafc;
  font-size: 15px;
  outline: none;
}
input:focus {
  border-color: #3b82f6;
}
button {
  padding: 12px;
  border-radius: 10px;
  border: none;
  background: #3b82f6;
  color: #fff;
  font-size: 15px;
  font-weight: 600;
  cursor: pointer;
}
button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.error {
  color: #f87171;
  font-size: 13px;
  margin-top: 8px;
}
</style>
