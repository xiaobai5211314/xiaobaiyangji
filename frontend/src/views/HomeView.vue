<template>
  <div>
    <!-- 标题 -->
    <div style="text-align: center; padding: 16px 0 10px;">
      <h2 style="margin: 0; font-size: 20px; font-weight: 800; background: linear-gradient(135deg, #ff5fa2 0%, #8b5cf6 48%, #38bdf8 100%); -webkit-background-clip: text; -webkit-text-fill-color: transparent; letter-spacing: 1px;">
        小白养基 v2
      </h2>
      <div style="font-size: 12px; color: #a8b3cf; margin-top: 4px;">{{ username || '未登录' }} · {{ activeFunds.length }} 支持仓</div>
    </div>

    <!-- 总览 -->
    <PortfolioSummary :summary="summary" />

    <!-- 收益曲线 -->
    <ProfitChart :points="chartPoints" :index-name="indexName" />

    <!-- 持仓标题 -->
    <div style="display: flex; justify-content: space-between; align-items: center; margin: 0 0 10px;">
      <div style="font-size: 16px; font-weight: bold; color: #f8fafc; display: flex; align-items: center; gap: 8px;">
        🛡️ 总持仓
        <span style="font-size: 12px; font-weight: normal; color: #a8b3cf;">(共 {{ activeFunds.length }} 支)</span>
      </div>
    </div>

    <!-- 基金卡片网格 -->
    <div class="holdings-grid">
      <FundCard v-for="fund in activeFunds" :key="fund.code" :fund="fund" />
    </div>

    <!-- 回本幅度榜 -->
    <RecoveryRank :list="recoveryList" />

    <!-- 加载状态 -->
    <div v-if="loading" style="text-align: center; padding: 60px 0; color: #a8b3cf;">
      <t-loading size="large" text="加载中..." />
    </div>

    <div v-if="error" style="text-align: center; padding: 20px; color: #ff4d4f; font-size: 13px;">{{ error }}</div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import PortfolioSummary from '../components/PortfolioSummary.vue'
import FundCard from '../components/FundCard.vue'
import ProfitChart from '../components/ProfitChart.vue'
import RecoveryRank from '../components/RecoveryRank.vue'
import { fetchTodayFunds, fetchPerformanceCurve } from '../api/fundApi.js'
import { normalizeFundForDashboard, calcActivePortfolioSummary, isActiveHoldingFund, calcBreakEvenRate } from '../utils/fundNormalize.js'

const loading = ref(true)
const error = ref('')
const fundsList = ref([])
const chartPoints = ref([])
const indexName = ref('沪深300')
const username = ref(localStorage.getItem('fund_username') || '')

const activeFunds = computed(() => fundsList.value.filter(isActiveHoldingFund))
const summary = computed(() => calcActivePortfolioSummary(activeFunds.value))

const recoveryList = computed(() =>
  activeFunds.value
    .map(f => {
      const hp = Number(f.holdingProfit)
      const ber = Number.isFinite(hp) && hp < 0 ? calcBreakEvenRate(f) : 0
      return { ...f, breakEvenRate: ber }
    })
    .filter(f => {
      const hp = Number(f.holdingProfit)
      const mv = Number(f.marketValue)
      return Number.isFinite(hp) && hp < 0 && Number.isFinite(mv) && mv > 0 && f.breakEvenRate > 0
    })
    .sort((a, b) => b.breakEvenRate - a.breakEvenRate)
)

const loadData = async () => {
  loading.value = true
  error.value = ''
  try {
    const user = username.value
    if (!user) { error.value = '请先在旧版登录后刷新此页'; loading.value = false; return }
    const data = await fetchTodayFunds(user)
    const rawFunds = Array.isArray(data) ? data : (data.funds || data.data || [])
    fundsList.value = rawFunds.map(f => normalizeFundForDashboard(f))
    try {
      const perf = await fetchPerformanceCurve(user, 'today')
      if (perf?.points) chartPoints.value = perf.points
      if (perf?.indexName) indexName.value = perf.indexName
    } catch { /* 曲线失败不影响主页 */ }
  } catch (e) {
    error.value = `加载失败: ${e.message}`
  } finally {
    loading.value = false
  }
}

onMounted(loadData)
</script>
