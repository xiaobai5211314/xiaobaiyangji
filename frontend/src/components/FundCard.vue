<template>
  <div class="glass-card fund-card">
    <!-- 头部：名称 + 按钮 -->
    <div style="display: flex; justify-content: space-between; align-items: start;">
      <div style="font-weight: bold; color: #f8fafc; font-size: 15px; max-width: 80%; min-width: 0;">
        <div style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">{{ fund.name }}</div>
        <div style="font-size: 11px; color: #a8b3cf; font-weight: normal; margin-top: 4px; font-family: monospace; background: rgba(0,0,0,.2); padding: 2px 6px; border-radius: 4px; width: fit-content;">
          {{ fund.code }}
        </div>
        <div style="margin-top: 4px; display: flex; gap: 6px; flex-wrap: wrap; align-items: center;">
          <span v-if="fund.shares > 0" class="xb-tag xb-tag--success xb-tag--compact">份额 {{ Number(fund.shares).toFixed(2) }}</span>
          <span v-if="Number(fund.todayPendingBuyAmount || 0) > 0"
                class="xb-tag xb-tag--warning xb-tag--compact"
                title="今天买入但未确认份额，不参与今日收益和今日收益率">
            待确认 {{ Number(fund.todayPendingBuyAmount).toFixed(0) }}
          </span>
        </div>
      </div>
      <div style="display: flex; gap: 5px; flex-shrink: 0; align-items: center;">
        <t-tooltip content="查看官方档案">
          <t-button shape="circle" size="small" variant="outline" style="--td-button-bg-color: rgba(56,189,248,.18); --td-button-border-color: rgba(56,189,248,.3); --td-button-text-color: #38bdf8;">📈</t-button>
        </t-tooltip>
        <t-tooltip content="回本路径模拟">
          <t-button shape="circle" size="small" variant="outline" style="--td-button-bg-color: rgba(251,191,36,.18); --td-button-border-color: rgba(251,191,36,.35); --td-button-text-color: #fbbf24;">🧭</t-button>
        </t-tooltip>
      </div>
    </div>

    <!-- 状态行 -->
    <div style="display: flex; align-items: center; gap: 5px; flex-wrap: wrap; padding-bottom: 6px; border-bottom: 1px dashed rgba(255,255,255,.1);">
      <span v-if="fund.isHoliday" class="xb-tag xb-tag--ghost" style="font-weight: bold;">☕ 休市</span>
      <span v-else :style="{ color: fund.todayRate >= 0 ? '#ff4d4f' : '#10b981', fontWeight: 900, fontSize: '14px' }">
        {{ fund.todayRate > 0 ? '+' : '' }}{{ Number(fund.todayRate || 0).toFixed(2) }}%
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

    <!-- 指标区 — 旧版 3x2 网格 -->
    <div class="metrics-grid">
      <div>
        <div class="metric-label">💰 持仓本金</div>
        <div class="metric-value" :style="{ color: Number(fund.costAmount || 0) > 0 ? '#cbd5e1' : '#eab308' }">
          {{ Number(fund.costAmount || 0) > 0 ? formatAmount(fund.costAmount) : '未设置' }}
        </div>
      </div>
      <div style="background: rgba(16,185,129,.08); border-radius: 4px; padding: 2px;">
        <div class="metric-label" style="color: #10b981;">💎 今日市值</div>
        <div class="metric-value" style="color: #f8fafc;">{{ formatAmount(fund.estimatedConfirmedHoldingAmount) }}</div>
      </div>
      <div style="background: rgba(56,189,248,.08); border-radius: 4px; padding: 2px;">
        <div class="metric-label" style="color: #38bdf8;">🌟 今日收益</div>
        <div class="metric-value" :style="{ color: profitColor(fund.todayProfit) }">{{ signed(fund.todayProfit) }}</div>
      </div>
      <div>
        <div class="metric-label">🚀 今日收益率</div>
        <div class="metric-value" :style="{ color: profitColor(fund.todayRate) }">{{ signed(fund.todayRate) }}%</div>
      </div>
      <div>
        <div class="metric-label">💰 累计盈亏</div>
        <div class="metric-value" :style="{ color: profitColor(fund.holdingProfit) }">{{ signed(fund.holdingProfit) }}</div>
      </div>
      <div>
        <div class="metric-label">📈 累计收益率</div>
        <div class="metric-value" :style="{ color: profitColor(fund.holdingRate) }">{{ signed(fund.holdingRate) }}%</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { formatAmount, signed, profitColor } from '../utils/format.js'

defineProps({
  fund: { type: Object, required: true }
})
</script>
