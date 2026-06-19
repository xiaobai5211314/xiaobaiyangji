<template>
  <div class="glass-card fund-card" :class="cardToneClass(fund.todayProfit)">
    <div class="fund-card__header">
      <div class="fund-card__title">
        <div class="fund-card__name" :title="fund.name">{{ fund.name }}</div>
        <div class="fund-card__meta">
          <span class="fund-code">{{ fund.code }}</span>
          <span v-if="fund.shares > 0" class="xb-tag xb-tag--success xb-tag--compact">份额 {{ Number(fund.shares).toFixed(2) }}</span>
          <span v-if="Number(fund.todayPendingBuyAmount || 0) > 0"
                class="xb-tag xb-tag--warning xb-tag--compact"
                title="今天买入但未确认份额，不参与今日收益和今日收益率">
            待确认 {{ Number(fund.todayPendingBuyAmount).toFixed(0) }}
          </span>
        </div>
      </div>
      <div class="card-actions">
        <t-tooltip content="查看官方档案">
          <t-button class="card-action-button--info" shape="circle" size="small" variant="outline">📈</t-button>
        </t-tooltip>
        <t-tooltip content="回本路径模拟">
          <t-button class="card-action-button--warning" shape="circle" size="small" variant="outline">🧭</t-button>
        </t-tooltip>
      </div>
    </div>

    <div class="fund-status-row">
      <span v-if="fund.isHoliday" class="xb-tag xb-tag--ghost xb-tag--compact">☕ 休市</span>
      <span v-else class="num fund-rate" :class="toneClass(fund.todayRate)">
        {{ signed(fund.todayRate) }}%
      </span>
      <span v-if="fund.isStaleQuote && !fund.isSettled" class="xb-tag xb-tag--warning">旧估值</span>
      <span v-if="fund.isSettled && (fund.actualRate != null || fund.lastSettledRate != null)"
            class="xb-tag xb-tag--info"
            title="盘后基金公司确认后的真实净值涨跌幅">
        正式净值 {{ (Number(fund.actualRate ?? fund.lastSettledRate) >= 0 ? '+' : '') + Number(fund.actualRate ?? fund.lastSettledRate).toFixed(2) }}%
      </span>
      <span v-if="fund.calibrationOffset && Math.abs(fund.calibrationOffset) > 0 && !fund.isHoliday"
            class="xb-tag xb-tag--primary">
        🤖修正: {{ fund.calibrationOffset > 0 ? '+' : '' }}{{ fund.calibrationOffset }}%
      </span>
      <span v-if="Math.abs(fund.diffRate || 0) > 0.15 && !fund.isHoliday" class="xb-tag xb-tag--danger">
        🚨 偏离: {{ fund.diffRate }}%
      </span>
    </div>

    <div class="fund-today-hero" :class="heroToneClass(fund.todayProfit)">
      <div>
        <div class="fund-today-title">今日收益</div>
        <div class="num fund-today-value" :class="toneClass(fund.todayProfit)">
          {{ signed(fund.todayProfit) }}
        </div>
      </div>
      <div class="num fund-today-rate" :class="toneClass(fund.todayRate)">
        {{ signed(fund.todayRate) }}%
      </div>
    </div>

    <div class="metrics-grid">
      <div class="metric-cell">
        <div class="metric-label">💰 持仓本金</div>
        <div class="num metric-value" :class="Number(fund.costAmount || 0) > 0 ? '' : 'value-pending'">
          {{ Number(fund.costAmount || 0) > 0 ? formatAmount(fund.costAmount) : '未设置' }}
        </div>
      </div>
      <div class="metric-cell">
        <div class="metric-label">💎 确认市值</div>
        <div class="num metric-value">{{ formatAmount(fund.estimatedConfirmedHoldingAmount) }}</div>
      </div>
      <div class="metric-cell metric-stack--pending">
        <div class="metric-label">今日待确认</div>
        <div class="num metric-value value-pending">{{ formatAmount(fund.todayPendingBuyAmount || 0) }}</div>
      </div>
      <div class="metric-cell">
        <div class="metric-label">💰 累计盈亏</div>
        <div class="num metric-value" :class="toneClass(fund.holdingProfit)">{{ signed(fund.holdingProfit) }}</div>
      </div>
      <div class="metric-cell">
        <div class="metric-label">📈 累计收益率</div>
        <div class="num metric-value" :class="toneClass(fund.holdingRate)">{{ signed(fund.holdingRate) }}%</div>
      </div>
    </div>

    <div v-if="Number(fund.todayPendingBuyAmount || 0) > 0" class="fund-note">
      待确认买入只展示金额，不计入今日收益和今日收益率。
    </div>
  </div>
</template>

<script setup>
import { formatAmount, signed } from '../utils/format.js'

const toNumber = (value) => {
  const n = Number(value)
  return Number.isFinite(n) ? n : 0
}

const toneClass = (value) => {
  const n = toNumber(value)
  if (n === 0) return 'value-flat'
  return n > 0 ? 'value-gain' : 'value-loss'
}

const cardToneClass = (value) => {
  const n = toNumber(value)
  if (n === 0) return 'fund-card--flat'
  return n > 0 ? 'fund-card--gain' : 'fund-card--loss'
}

const heroToneClass = (value) => {
  const n = toNumber(value)
  if (n === 0) return 'fund-today-hero--flat'
  return n > 0 ? 'fund-today-hero--gain' : 'fund-today-hero--loss'
}

defineProps({
  fund: { type: Object, required: true }
})
</script>
