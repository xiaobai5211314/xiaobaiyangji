<template>
  <div class="glass-card" style="margin-bottom: 12px;">
    <div style="text-align: center; margin-bottom: 12px;">
      <div style="font-size: 13px; color: #a8b3cf; margin-bottom: 4px; font-weight: bold;" title="确认持仓 + 今日待确认买入">
        💎 账户总金额
      </div>
      <div class="num" style="font-size: 26px; font-weight: 900; color: #f8fafc;">
        ¥ {{ formatAmount(summary.accountTotalAmount) }}
      </div>
      <div v-if="summary.todayPendingBuyTotal > 0" style="font-size: 11px; color: #a8b3cf; margin-top: 3px;">
        <span title="不含今日待确认买入">确认持仓 ¥{{ formatAmount(summary.confirmedHoldingTotalAmount) }}</span>
        ·
        <span title="今天买入但未确认份额，不参与今日收益和今日收益率">今日待确认 ¥{{ formatAmount(summary.todayPendingBuyTotal) }}</span>
      </div>
    </div>
    <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; text-align: center;">
      <div>
        <div style="font-size: 11px; color: #a8b3cf;">今日总收益</div>
        <div class="num" :style="{ color: profitColor(summary.totalTodayProfit), fontWeight: 'bold', fontSize: '15px' }">
          {{ signed(summary.totalTodayProfit) }}
        </div>
      </div>
      <div>
        <div style="font-size: 11px; color: #a8b3cf;">今日收益率</div>
        <div class="num" :style="{ color: profitColor(summary.totalTodayRate), fontWeight: 'bold', fontSize: '15px' }">
          {{ signed(summary.totalTodayRate) }}%
        </div>
      </div>
      <div>
        <div style="font-size: 11px; color: #a8b3cf;">累计收益率</div>
        <div class="num" :style="{ color: profitColor(summary.totalRate), fontWeight: 'bold', fontSize: '15px' }">
          {{ signed(summary.totalRate) }}%
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { formatAmount, signed, profitColor } from '../utils/format.js'

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
