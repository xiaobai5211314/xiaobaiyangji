<template>
  <div>
    <div style="text-align: center; padding: 16px 0 8px;">
      <h2 style="margin: 0; font-size: 20px; font-weight: 800; background: linear-gradient(135deg, #ff5fa2 0%, #8b5cf6 48%, #38bdf8 100%); -webkit-background-clip: text; -webkit-text-fill-color: transparent;">
        小白养基 v2
      </h2>
      <div style="font-size: 12px; color: #a8b3cf; margin-top: 4px;">{{ username || '未登录' }} · {{ activeFunds.length }} 支持仓</div>
    </div>

    <PortfolioSummary :summary="summary" />

    <ProfitChart :points="chartPoints" :index-name="indexName" />

    <div style="display: flex; justify-content: space-between; align-items: center; margin: 0 0 10px;">
      <div style="font-size: 16px; font-weight: bold; color: #f8fafc; display: flex; align-items: center; gap: 8px;">
        🛡️ 总持仓
        <span style="font-size: 12px; font-weight: normal; color: #a8b3cf;">(共 {{ activeFunds.length }} 支)</span>
      </div>
    </div>

    <div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 12px; margin-bottom: 16px;">
      <FundCard v-for="fund in activeFunds" :key="fund.code" :fund="fund" />
    </div>

    <RecoveryRank :list="recoveryList" />

    <div v-if="loading" style="text-align: center; padding: 40px 0; color: #a8b3cf;">
      <t-loading size="large" text="加载中..." />
    </div>

    <div v-if="error" style="text-align: center; padding: 20px; color: #ef4444; font-size: 13px;">{{ error }}</div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import PortfolioSummary from '../components/PortfolioSummary.vue'
import FundCard from '../components/FundCard.vue'
import ProfitChart from '../components/ProfitChart.vue'
import RecoveryRank from '../components/RecoveryRank.vue'
import { fetchTodayFunds, fetchPerformanceCurve } from '../api/fundApi.js'
import { normalizeFundForDashboard, calcActivePortfolioSummary, isActiveHoldingFund, isClearedFund, calcBreakEvenRate } from '../utils/fundNormalize.js'

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
      const holdingProfit = Number(f.holdingProfit)
      const breakEvenRate = Number.isFinite(holdingProfit) && holdingProfit < 0
        ? calcBreakEvenRate(f)
        : 0
      return { ...f, breakEvenRate }
    })
    .filter(f => {
      const holdingProfit = Number(f.holdingProfit)
      const marketValue = Number(f.marketValue)
      return Number.isFinite(holdingProfit) && holdingProfit < 0 &&
             Number.isFinite(marketValue) && marketValue > 0 &&
             Number.isFinite(f.breakEvenRate) && f.breakEvenRate > 0
    })
    .sort((a, b) => b.breakEvenRate - a.breakEvenRate)
)

const loadData = async () => {
  loading.value = true
  error.value = ''
  try {
    const user = username.value
    if (!user) {
      error.value = '请先登录'
      loading.value = false
      return
    }
    const data = await fetchTodayFunds(user)
    const rawFunds = Array.isArray(data) ? data : (data.funds || data.data || [])
    fundsList.value = rawFunds.map(f => normalizeFundForDashboard(f))
    try {
      const perf = await fetchPerformanceCurve(user, 'today')
      if (perf?.points) chartPoints.value = perf.points
      if (perf?.indexName) indexName.value = perf.indexName
    } catch { /* 曲线加载失败不影响主页 */ }
  } catch (e) {
    error.value = `加载失败: ${e.message}`
  } finally {
    loading.value = false
  }
}

onMounted(loadData)
</script>
