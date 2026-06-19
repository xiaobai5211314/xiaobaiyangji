<template>
  <div>
    <div class="page-heading">
      <h2 class="app-title">
        小白养基 v2
      </h2>
      <div class="page-subtitle">{{ username || '未登录' }} · {{ activeFunds.length }} 支持仓</div>
    </div>

    <!-- 总览 -->
    <PortfolioSummary :summary="summary" />

    <!-- 收益曲线 -->
    <ProfitChart :points="chartPoints" :index-name="indexName" />

    <div class="section-heading">
      <div class="section-title">
        🛡️ 总持仓
        <span class="section-count">(共 {{ activeFunds.length }} 支)</span>
      </div>
    </div>

    <!-- 基金卡片网格 -->
    <div class="holdings-grid">
      <FundCard v-for="fund in activeFunds" :key="fund.code" :fund="fund" />
    </div>

    <!-- 回本幅度榜 -->
    <RecoveryRank :list="recoveryList" />

    <div v-if="loading" class="state-panel">
      <t-loading size="large" text="加载中..." />
    </div>

    <div v-if="error" class="state-panel state-panel--error">{{ error }}</div>
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
