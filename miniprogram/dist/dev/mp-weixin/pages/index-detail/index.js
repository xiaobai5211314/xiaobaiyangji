"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_sector = require("../../services/api/sector.js");
const utils_format = require("../../utils/format.js");
const stores_theme = require("../../stores/theme.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const loading = common_vendor.ref(false);
    const indexName = common_vendor.ref("");
    const indexCode = common_vendor.ref("");
    const rows = common_vendor.ref([]);
    const currentIndex = common_vendor.computed(() => {
      const name = indexName.value;
      const code = indexCode.value;
      return rows.value.find((item) => String(item.code || "") === code && code) || rows.value.find((item) => String(item.name || "") === name && name) || rows.value.find((item) => cleanIndexName(item.name) === cleanIndexName(name) && name) || {};
    });
    const historyRows = common_vendor.computed(() => indexHistoryRows(currentIndex.value));
    common_vendor.onLoad((query) => {
      stores_theme.loadTheme();
      indexName.value = decodeURIComponent(String((query == null ? void 0 : query.indexName) || ""));
      indexCode.value = decodeURIComponent(String((query == null ? void 0 : query.indexCode) || ""));
      loadData(false).catch((error) => console.warn("[index-detail:load]", error));
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        await loadData(true);
      } catch (error) {
        console.warn("[index-detail:pull-down-refresh]", error);
        common_vendor.index.showToast({ title: "刷新失败，请稍后重试", icon: "none" });
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadData(force = false) {
      if (loading.value)
        return;
      loading.value = true;
      try {
        const data = await services_api_sector.getGlobalIndices(force);
        rows.value = Array.isArray(data) ? data.filter(hasIndexEntry) : [];
      } finally {
        loading.value = false;
      }
    }
    function indexHistoryRows(item) {
      const rows2 = getHistoryList(item);
      const sortedRows = [...rows2].sort((a, b) => normalizeDateText(b).localeCompare(normalizeDateText(a)));
      let derivedClose = indexPointValue(item);
      return sortedRows.map((row) => {
        const normalized = normalizeIndexHistoryItem(row, derivedClose);
        if (normalized.close !== null) {
          const rateForPrev = normalized.rate;
          if (rateForPrev !== null && rateForPrev !== -100) {
            derivedClose = normalized.close / (1 + rateForPrev / 100);
          } else {
            derivedClose = null;
          }
        }
        return {
          raw: row,
          dateText: normalized.dateText,
          closeText: normalized.close === null ? "--" : normalized.close.toFixed(2),
          rate: normalized.rate,
          rateText: normalized.rate === null ? "--" : utils_format.signedPercent(normalized.rate),
          pointSource: normalized.pointSource
        };
      }).filter((row) => row.dateText !== "--" || row.rate !== null);
    }
    function hasIndexEntry(item) {
      const name = String(item.name || "");
      return Boolean(cleanIndexName(name));
    }
    function numericOrDash(value) {
      const n = Number(value);
      return Number.isFinite(n) ? n.toFixed(2) : "--";
    }
    function finiteNumber(value) {
      if (!isPresent(value))
        return null;
      const n = Number(value);
      return Number.isFinite(n) ? n : null;
    }
    function normalizeIndexHistoryItem(row, fallbackClose) {
      const source = row;
      const directClose = firstIndexPoint(
        source.point,
        source.latest,
        source.close,
        source.value,
        source.indexValue,
        source.current,
        source.price
      );
      const rate = firstFinite(source.todayRate, source.rate, source.changePercent, source.pct, source.pctChg);
      const dateText = String(
        firstKnown(source.date, source.tradeDate, source.time, source.day, source.datetime) || "--"
      ).slice(0, 10);
      if (directClose !== null) {
        return { dateText, close: directClose, rate, pointSource: "direct" };
      }
      return {
        dateText,
        close: fallbackClose,
        rate,
        pointSource: fallbackClose === null ? "missing" : "derivedFromLatestAndRate"
      };
    }
    function normalizeDateText(row) {
      const source = row;
      return String(firstKnown(source.date, source.tradeDate, source.time, source.day, source.datetime) || "");
    }
    function cleanIndexName(value) {
      return String(value || "").replace(/\s*\((?:无数据|异常:.*)\)\s*$/g, "").trim();
    }
    function displayIndexName(item) {
      return cleanIndexName(item.name);
    }
    function indexPointText(item) {
      const value = indexPointValue(item);
      return value !== null ? numericOrDash(value) : "--";
    }
    function indexRateValue(value, item) {
      const source = item;
      return firstFinite(value, source.rate, source.changePercent, source.pct, source.pctChg);
    }
    function indexPercentText(value, item) {
      const n = indexRateValue(value, item);
      return n === null ? "--" : utils_format.signedPercent(n);
    }
    function indexPointValue(item) {
      const source = item;
      return firstIndexPoint(
        source.point,
        source.latest,
        source.close,
        source.value,
        source.indexValue,
        source.current,
        source.price
      );
    }
    function indexYearRateValue(item) {
      const source = item;
      return firstFinite(source.yearRate, source.oneYearRate, source.annualRate, source.yearChangePercent);
    }
    function indexYearPercentText(item) {
      const n = indexYearRateValue(item);
      return n === null ? "--" : utils_format.signedPercent(n);
    }
    function getHistoryList(item) {
      const source = item;
      const rows2 = firstArray(source.klines, source.lines, source.history, source.series, source.data, source.items, source.list);
      return rows2;
    }
    function firstFinite(...values) {
      for (const value of values) {
        const n = finiteNumber(value);
        if (n !== null)
          return n;
      }
      return null;
    }
    function firstIndexPoint(...values) {
      for (const value of values) {
        const n = finiteNumber(value);
        if (n !== null && n > 0)
          return n;
      }
      return null;
    }
    function firstKnown(...values) {
      for (const value of values) {
        if (value !== null && value !== void 0 && value !== "")
          return value;
      }
      return null;
    }
    function firstArray(...values) {
      for (const value of values) {
        if (Array.isArray(value))
          return value;
      }
      return [];
    }
    function isPresent(value) {
      return value !== null && value !== void 0 && !(typeof value === "string" && value.trim() === "");
    }
    function historyKey(row, index) {
      return `${row.dateText}-${index}`;
    }
    function goBack() {
      common_vendor.index.navigateBack({
        fail: () => common_vendor.index.reLaunch({ url: "/pages/sector/index" })
      });
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: common_vendor.t(displayIndexName(currentIndex.value) || "指数详情"),
        b: common_vendor.o(goBack, "0c"),
        c: !currentIndex.value.name && !loading.value
      }, !currentIndex.value.name && !loading.value ? {} : common_vendor.e({
        d: common_vendor.t(indexPointText(currentIndex.value)),
        e: common_vendor.t(indexPercentText(currentIndex.value.todayRate, currentIndex.value)),
        f: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(indexRateValue(currentIndex.value.todayRate, currentIndex.value))),
        g: common_vendor.t(indexYearPercentText(currentIndex.value)),
        h: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(indexYearRateValue(currentIndex.value))),
        i: common_vendor.t(historyRows.value.length),
        j: historyRows.value.length === 0
      }, historyRows.value.length === 0 ? {} : {}, {
        k: common_vendor.f(historyRows.value, (row, index, i0) => {
          return {
            a: common_vendor.t(row.dateText),
            b: common_vendor.t(row.closeText),
            c: common_vendor.t(row.rateText),
            d: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(row.rate)),
            e: historyKey(row, index)
          };
        }),
        l: currentIndex.value.updatedAt
      }, currentIndex.value.updatedAt ? {
        m: common_vendor.t(currentIndex.value.updatedAt)
      } : {}), {
        n: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-e40632be"]]);
wx.createPage(MiniProgramPage);
