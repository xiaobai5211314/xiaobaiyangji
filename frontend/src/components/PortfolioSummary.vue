<template>
  <div class="glass-card glass-card--hero portfolio-summary">
    <div class="summary-hero">
      <div class="summary-label" title="确认持仓 + 今日待确认买入">
        💎 账户总金额
      </div>
      <div class="num summary-amount">
        ¥ {{ formatAmount(summary.accountTotalAmount) }}
      </div>
    </div>

    <div class="summary-breakdown">
      <div class="summary-breakdown-item" title="不含今日待确认买入">
        <div class="metric-label">确认持仓</div>
        <div class="metric-value">¥{{ formatAmount(summary.confirmedHoldingTotalAmount) }}</div>
      </div>
      <div class="summary-breakdown-item summary-breakdown-item--pending" title="今天买入但未确认份额，不参与今日收益和今日收益率">
        <div class="metric-label">今日待确认</div>
        <div class="metric-value value-pending">¥{{ formatAmount(summary.todayPendingBuyTotal) }}</div>
      </div>
    </div>

    <div class="metric-row">
      <div class="metric-stack">
        <div class="metric-label">今日收益</div>
        <div class="num metric-value metric-value--large" :class="toneClass(summary.totalTodayProfit)">
          {{ signed(summary.totalTodayProfit) }}
        </div>
      </div>
      <div class="metric-stack">
        <div class="metric-label">今日收益率</div>
        <div class="num metric-value metric-value--large" :class="toneClass(summary.totalTodayRate)">
          {{ signed(summary.totalTodayRate) }}%
        </div>
      </div>
      <div class="metric-stack">
        <div class="metric-label">累计收益率</div>
        <div class="num metric-value metric-value--large" :class="toneClass(summary.totalRate)">
          {{ signed(summary.totalRate) }}%
        </div>
      </div>
    </div>

    <div v-if="summary.todayPendingBuyTotal > 0" class="summary-note">
      今日待确认买入只展示金额，不参与今日收益和今日收益率计算。
    </div>
  </div>
</template>

<script setup>
import { formatAmount, signed } from '../utils/format.js'

const toneClass = (value) => {
  const n = Number(value)
  if (!Number.isFinite(n) || n === 0) return 'value-flat'
  return n > 0 ? 'value-gain' : 'value-loss'
}

defineProps({
  summary: {
    type: Object,
    default: () => ({
      accountTotalAmount: 0,
      confirmedHoldingTotalAmount: 0,
      todayPendingBuyTotal: 0,
      totalTodayProfit: 0,
      totalTodayRate: 0,
      totalProfit: 0,
      totalRate: 0
    })
  }
})
</script>
