"use strict";
const common_vendor = require("../common/vendor.js");
const chartWidth = 560;
const chartHeight = 116;
const paddingX = 18;
const paddingY = 14;
const lineWidth = 4;
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "SparklineChart",
  props: {
    canvasId: {},
    points: {},
    tone: { default: "neutral" },
    emptyText: { default: "暂无足够走势数据" }
  },
  setup(__props) {
    const props = __props;
    const rawValues = common_vendor.computed(() => {
      return props.points.map((point) => Number(point)).filter((point) => Number.isFinite(point));
    });
    const values = common_vendor.computed(() => smoothValues(downsample(rawValues.value, 96)));
    const chartPoints = common_vendor.computed(() => normalizePoints(values.value));
    const hasData = common_vendor.computed(() => chartPoints.value.length > 1);
    const emptyText = common_vendor.computed(() => props.emptyText);
    const segments = common_vendor.computed(() => {
      const points = chartPoints.value;
      if (points.length <= 1)
        return [];
      const rows = [];
      for (let index = 1; index < points.length; index += 1) {
        const prev = points[index - 1];
        const current = points[index];
        const dx = current.x - prev.x;
        const dy = current.y - prev.y;
        const length = Math.sqrt(dx * dx + dy * dy);
        if (!Number.isFinite(length) || length <= 0)
          continue;
        rows.push({
          x: prev.x,
          y: prev.y,
          length,
          angle: Math.atan2(dy, dx) * (180 / Math.PI)
        });
      }
      return rows;
    });
    const lastPoint = common_vendor.computed(() => chartPoints.value[chartPoints.value.length - 1] || { x: 0, y: chartHeight / 2 });
    function lineColor() {
      if (props.tone === "profit")
        return "#ff4d4f";
      if (props.tone === "loss")
        return "#10b981";
      return "#60a5fa";
    }
    function normalizePoints(data) {
      if (data.length <= 1)
        return [];
      const min = Math.min(...data);
      const max = Math.max(...data);
      const isFlat = max === min;
      const range = isFlat ? 1 : max - min;
      const step = (chartWidth - paddingX * 2) / (data.length - 1);
      return data.map((value, index) => {
        const x = paddingX + index * step;
        const y = isFlat ? chartHeight / 2 : chartHeight - paddingY - (value - min) / range * (chartHeight - paddingY * 2);
        return {
          x: clamp(x, 0, chartWidth),
          y: clamp(y, 0, chartHeight)
        };
      });
    }
    function segmentStyle(segment) {
      return [
        `left:${segment.x}rpx`,
        `top:${segment.y - lineWidth / 2}rpx`,
        `width:${segment.length}rpx`,
        `height:${lineWidth}rpx`,
        `background:${lineColor()}`,
        `transform:rotate(${segment.angle}deg)`,
        `box-shadow:0 0 12rpx ${lineColor()}`
      ].join(";");
    }
    function dotStyle(point) {
      return [
        `left:${point.x - 6}rpx`,
        `top:${point.y - 6}rpx`,
        `background:${lineColor()}`,
        `box-shadow:0 0 16rpx ${lineColor()}`
      ].join(";");
    }
    function downsample(data, maxPoints) {
      if (data.length <= maxPoints)
        return data;
      const result = [];
      const last = data.length - 1;
      for (let index = 0; index < maxPoints; index += 1) {
        const sourceIndex = Math.round(index / (maxPoints - 1) * last);
        result.push(data[sourceIndex]);
      }
      return result;
    }
    function smoothValues(data) {
      if (data.length < 5)
        return data;
      return data.map((value, index) => {
        if (index === 0 || index === data.length - 1)
          return value;
        const prev = data[index - 1];
        const next = data[index + 1];
        return (prev + value * 2 + next) / 4;
      });
    }
    function clamp(value, min, max) {
      return Math.min(max, Math.max(min, value));
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: hasData.value
      }, hasData.value ? {
        b: common_vendor.f(segments.value, (segment, index, i0) => {
          return {
            a: `${_ctx.canvasId}-${index}`,
            b: common_vendor.s(segmentStyle(segment))
          };
        }),
        c: common_vendor.s(dotStyle(lastPoint.value))
      } : {
        d: common_vendor.t(emptyText.value)
      }, {
        e: _ctx.canvasId
      });
    };
  }
});
const Component = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-9dbb36eb"]]);
wx.createComponent(Component);
