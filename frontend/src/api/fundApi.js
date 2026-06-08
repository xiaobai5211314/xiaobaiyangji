const API_BASE = import.meta.env.VITE_API_BASE || ''

const request = async (url, options = {}) => {
  const res = await fetch(`${API_BASE}${url}`, options)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
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
