const API_BASE = import.meta.env.VITE_API_BASE || ''

const TOKEN_KEY = 'xiaobai_token'
const USERNAME_KEY = 'xiaobai_username'
const DISPLAY_NAME_KEY = 'xiaobai_display_name'

export function getStoredToken() {
  return localStorage.getItem(TOKEN_KEY) || ''
}

export function getStoredUsername() {
  return localStorage.getItem(USERNAME_KEY) || ''
}

export function getStoredDisplayName() {
  return localStorage.getItem(DISPLAY_NAME_KEY) || ''
}

export function saveAuth(data) {
  if (data.token) localStorage.setItem(TOKEN_KEY, data.token)
  if (data.username) localStorage.setItem(USERNAME_KEY, data.username)
  if (data.displayName) localStorage.setItem(DISPLAY_NAME_KEY, data.displayName)
}

export function clearAuth() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USERNAME_KEY)
  localStorage.removeItem(DISPLAY_NAME_KEY)
}

export function isLoggedIn() {
  return Boolean(getStoredToken() && getStoredUsername())
}

const request = async (url, options = {}) => {
  const token = getStoredToken()
  const headers = { Accept: 'application/json', ...options.headers }
  if (token) headers['Authorization'] = `Bearer ${token}`

  const res = await fetch(`${API_BASE}${url}`, { ...options, headers })
  if (res.status === 401) {
    clearAuth()
    window.location.hash = '#/login'
    throw new Error('未登录或登录已过期')
  }
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}

export const login = (username, password) => {
  const body = new URLSearchParams({ username, password })
  return request('/api/auth/login', {
    method: 'POST',
    body,
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
  })
}

export const register = (username, password) => {
  const body = new URLSearchParams({ username, password })
  return request('/api/auth/register', {
    method: 'POST',
    body,
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
  })
}

export const fetchTodayFunds = (username, force = false) => {
  const params = new URLSearchParams({ username })
  if (force) params.set('force', 'true')
  return request(`/api/fund/today?${params}`)
}

export const fetchPerformanceCurve = (username, period = 'today', index = 'hs300') => {
  return request(`/api/fund/performance?username=${encodeURIComponent(username)}&period=${period}&index=${index}`)
}

export const fetchAnalysis = (username, dateStr) => {
  let url = `/api/fund/analysis?username=${encodeURIComponent(username)}`
  if (dateStr) url += `&date=${dateStr}`
  return request(url)
}
