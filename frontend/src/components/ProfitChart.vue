<template>
  <div class="glass-card" style="margin-bottom: 12px;">
    <div style="font-size: 15px; font-weight: 900; color: #f8fafc; margin-bottom: 4px;">📈 今日收益曲线</div>
    <div ref="chartRef" style="width: 100%; height: 220px;"></div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue'
import * as echarts from 'echarts'

const props = defineProps({
  points: { type: Array, default: () => [] },
  indexName: { type: String, default: '沪深300' }
})

const chartRef = ref(null)
let chart = null

const renderChart = () => {
  if (!chartRef.value || !chart) return
  const times = props.points.map(p => p.time?.slice(11, 16) || '')
  const myRates = props.points.map(p => p.rate ?? 0)
  const indexRates = props.points.map(p => p.indexRate ?? null)

  chart.setOption({
    backgroundColor: 'transparent',
    grid: { left: 40, right: 15, top: 20, bottom: 30 },
    tooltip: { trigger: 'axis', backgroundColor: 'rgba(15,23,42,0.9)', borderColor: 'rgba(174,201,255,0.16)', textStyle: { color: '#f8fafc', fontSize: 12 } },
    xAxis: { type: 'category', data: times, axisLine: { lineStyle: { color: 'rgba(255,255,255,0.1)' } }, axisLabel: { color: '#a8b3cf', fontSize: 10 } },
    yAxis: { type: 'value', axisLine: { show: false }, splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)' } }, axisLabel: { color: '#a8b3cf', fontSize: 10, formatter: '{value}%' } },
    series: [
      { name: '我的收益率', type: 'line', data: myRates, smooth: true, symbol: 'none', lineStyle: { width: 2, color: '#8b5cf6' }, areaStyle: { color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [{ offset: 0, color: 'rgba(139,92,246,0.3)' }, { offset: 1, color: 'rgba(139,92,246,0.02)' }]) } },
      ...(indexRates.some(v => v !== null) ? [{ name: props.indexName, type: 'line', data: indexRates, smooth: true, symbol: 'none', lineStyle: { width: 1.5, color: '#f59e0b', type: 'dashed' } }] : [])
    ]
  })
}

onMounted(() => {
  chart = echarts.init(chartRef.value)
  renderChart()
  window.addEventListener('resize', () => chart?.resize())
})

onUnmounted(() => {
  chart?.dispose()
  chart = null
})

watch(() => props.points, () => nextTick(renderChart), { deep: true })
</script>
