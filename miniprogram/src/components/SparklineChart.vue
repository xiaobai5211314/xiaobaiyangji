<template>
  <view class="sparkline" :data-chart-id="canvasId">
    <view v-if="hasData" class="sparkline-plot">
      <view class="sparkline-midline" />
      <view
        v-for="(segment, index) in segments"
        :key="`${canvasId}-${index}`"
        class="sparkline-segment"
        :style="segmentStyle(segment)"
      />
      <view class="sparkline-dot" :style="dotStyle(lastPoint)" />
    </view>
    <view v-else class="sparkline-empty">{{ emptyText }}</view>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue';

interface ChartPoint {
  x: number;
  y: number;
}

interface ChartSegment extends ChartPoint {
  length: number;
  angle: number;
}

const props = withDefaults(
  defineProps<{
    canvasId: string;
    points: Array<number | string | null | undefined>;
    tone?: 'profit' | 'loss' | 'neutral';
    emptyText?: string;
  }>(),
  {
    tone: 'neutral',
    emptyText: '暂无足够走势数据'
  }
);

const chartWidth = 560;
const chartHeight = 116;
const paddingX = 18;
const paddingY = 14;
const lineWidth = 4;

const rawValues = computed(() => {
  return props.points
    .map((point) => Number(point))
    .filter((point) => Number.isFinite(point));
});

const values = computed(() => smoothValues(downsample(rawValues.value, 72)));
const chartPoints = computed(() => normalizePoints(values.value));
const hasData = computed(() => chartPoints.value.length > 1);
const emptyText = computed(() => props.emptyText);
const segments = computed(() => {
  const points = chartPoints.value;
  if (points.length <= 1) return [];

  const rows: ChartSegment[] = [];
  for (let index = 1; index < points.length; index += 1) {
    const prev = points[index - 1];
    const current = points[index];
    const dx = current.x - prev.x;
    const dy = current.y - prev.y;
    const length = Math.sqrt(dx * dx + dy * dy);
    if (!Number.isFinite(length) || length <= 0) continue;

    rows.push({
      x: prev.x,
      y: prev.y,
      length,
      angle: Math.atan2(dy, dx) * (180 / Math.PI)
    });
  }

  return rows;
});
const lastPoint = computed(() => chartPoints.value[chartPoints.value.length - 1] || { x: 0, y: chartHeight / 2 });

function lineColor() {
  if (props.tone === 'profit') return '#ff4d4f';
  if (props.tone === 'loss') return '#10b981';
  return '#60a5fa';
}

function normalizePoints(data: number[]) {
  if (data.length <= 1) return [];

  const min = Math.min(...data);
  const max = Math.max(...data);
  const isFlat = max === min;
  const range = isFlat ? 1 : max - min;
  const step = (chartWidth - paddingX * 2) / (data.length - 1);

  return data.map((value, index) => {
    const x = paddingX + index * step;
    const y = isFlat
      ? chartHeight / 2
      : chartHeight - paddingY - ((value - min) / range) * (chartHeight - paddingY * 2);

    return {
      x: clamp(x, 0, chartWidth),
      y: clamp(y, 0, chartHeight)
    };
  });
}

function segmentStyle(segment: ChartSegment) {
  return [
    `left:${segment.x}rpx`,
    `top:${segment.y - lineWidth / 2}rpx`,
    `width:${segment.length}rpx`,
    `height:${lineWidth}rpx`,
    `background:${lineColor()}`,
    `transform:rotate(${segment.angle}deg)`,
    `box-shadow:0 0 12rpx ${lineColor()}`
  ].join(';');
}

function dotStyle(point: ChartPoint) {
  return [
    `left:${point.x - 6}rpx`,
    `top:${point.y - 6}rpx`,
    `background:${lineColor()}`,
    `box-shadow:0 0 16rpx ${lineColor()}`
  ].join(';');
}

function downsample(data: number[], maxPoints: number) {
  if (data.length <= maxPoints) return data;

  const result: number[] = [];
  const last = data.length - 1;
  for (let index = 0; index < maxPoints; index += 1) {
    const sourceIndex = Math.round((index / (maxPoints - 1)) * last);
    result.push(data[sourceIndex]);
  }

  return result;
}

function smoothValues(data: number[]) {
  if (data.length < 5) return data;

  return data.map((value, index) => {
    if (index === 0 || index === data.length - 1) return value;
    const prev = data[index - 1];
    const next = data[index + 1];
    return (prev + value * 2 + next) / 4;
  });
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}
</script>

<style lang="scss" scoped>
@import '../styles/variables.scss';

.sparkline {
  width: 100%;
  height: 140rpx;
  overflow: hidden;
  border-radius: 24rpx;
  background:
    radial-gradient(circle at 14% 0%, rgba(255, 95, 162, 0.08), transparent 32%),
    radial-gradient(circle at 88% 0%, rgba(56, 189, 248, 0.08), transparent 30%),
    rgba(15, 23, 42, 0.32);
}

.sparkline-plot {
  position: relative;
  width: 560rpx;
  max-width: 100%;
  height: 140rpx;
  margin: 0 auto;
  overflow: hidden;
}

.sparkline-midline {
  position: absolute;
  left: 18rpx;
  right: 18rpx;
  top: 69rpx;
  height: 1rpx;
  background: rgba(148, 163, 184, 0.18);
}

.sparkline-segment {
  position: absolute;
  border-radius: 999rpx;
  transform-origin: 0 50%;
}

.sparkline-dot {
  position: absolute;
  width: 12rpx;
  height: 12rpx;
  border-radius: 50%;
}

.sparkline-empty {
  height: 140rpx;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  font-size: 22rpx;
}
</style>
