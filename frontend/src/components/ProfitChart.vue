<template>
  <div class="glass-card" style="margin-bottom: 14px;">
    <div style="font-size: 15px; font-weight: 900; color: #f8fafc; margin-bottom: 4px;">📈 今日收益曲线</div>
    <div style="font-size: 12px; color: #a8b3cf; margin-bottom: 8px;">我的收益率 vs {{ indexName }}</div>
    <div v-if="!points || points.length === 0" style="text-align: center; padding: 40px 0; color: #a8b3cf; font-size: 13px;">暂无曲线数据</div>
    <div v-else ref="chartRef" style="width: 100%; height: 320px;"></div>
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
    grid: { left: 45, right: 15, top: 20, bottom: 30 },
    tooltip: { trigger: 'axis', backgroundColor: 'rgba(15,23,42,.92)', borderColor: 'rgba(174,201,255,.16)', textStyle: { color: '#f8fafc', fontSize: 12 } },
    xAxis: { type: 'category', data: times, axisLine: { lineStyle: { color: 'rgba(255,255,255,.1)' } }, axisLabel: { color: '#a8b3cf', fontSize: 10 } },
    yAxis: { type: 'value', axisLine: { show: false }, splitLine: { lineStyle: { color: 'rgba(255,255,255,.06)' } }, axisLabel: { color: '#a8b3cf', fontSize: 10, formatter: '{value}%' } },
    series: [
      {
        name: '我的收益率', type: 'line', data: myRates, smooth: true, symbol: 'none',
        lineStyle: { width: 2, color: '#ff5fa2' },
        areaStyle: { color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
          { offset: 0, color: 'rgba(255,95,162,.25)' },
          { offset: 1, color: 'rgba(139,92,246,.02)' }
        ])}
      },
      ...(indexRates.some(v => v !== null) ? [{
        name: props.indexName, type: 'line', data: indexRates, smooth: true, symbol: 'none',
        lineStyle: { width: 1.5, color: '#38bdf8', type: 'dashed' }
      }] : [])
    ]
  })
}

onMounted(() => {
  if (chartRef.value) {
    chart = echarts.init(chartRef.value)
    renderChart()
    window.addEventListener('resize', () => chart?.resize())
  }
})

onUnmounted(() => { chart?.dispose(); chart = null })

watch(() => props.points, () => { if (chart) { nextTick(renderChart) } }, { deep: true })
</script>
