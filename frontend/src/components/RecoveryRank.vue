<template>
  <div class="glass-card" style="margin-bottom: 12px;">
    <div style="font-size: 15px; font-weight: 900; color: #f8fafc; margin-bottom: 4px;">🧭 回本所需涨幅</div>
    <div style="font-size: 12px; color: #a8b3cf; margin-bottom: 8px;">当前所有未回本持仓，按需涨幅度从高到低排序。</div>
    <div v-if="list.length === 0" style="color: #a8b3cf; font-size: 13px; padding: 14px 0; text-align: center;">
      当前没有需要回本的持仓。
    </div>
    <div v-for="fund in list" :key="fund.code" style="display: flex; justify-content: space-between; align-items: center; padding: 6px 0; border-bottom: 1px solid rgba(255,255,255,0.05);">
      <span style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 180px; color: #f8fafc;">{{ fund.name }}</span>
      <span style="font-weight: 900; color: #fbbf24;">需涨{{ formatPercent(fund.breakEvenRate) }}</span>
    </div>
    <div v-if="list.length > 0" style="font-size: 11px; color: #a8b3cf; padding: 8px 0 2px; opacity: 0.7;">
      回本所需涨幅 = 当前亏损金额 ÷ 当前市值 × 100%，不是累计收益率
    </div>
  </div>
</template>

<script setup>
import { formatPercent } from '../utils/format.js'

defineProps({
  list: { type: Array, default: () => [] }
})
</script>
