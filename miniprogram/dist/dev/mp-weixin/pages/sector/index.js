"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_sector = require("../../services/api/sector.js");
const utils_format = require("../../utils/format.js");
const stores_theme = require("../../stores/theme.js");
var define_import_meta_env_default = {};
if (!Math) {
  AppTabBar();
}
const AppTabBar = () => "../../components/AppTabBar.js";
const PAGE_CACHE_TTL = 6e4;
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const loading = common_vendor.ref(false);
    const sectorPayload = common_vendor.ref({});
    const flowPayload = common_vendor.ref({});
    const indices = common_vendor.ref([]);
    const loadedAt = common_vendor.ref(0);
    const DEBUG_FIELD_AUDIT = (define_import_meta_env_default == null ? void 0 : define_import_meta_env_default.VITE_DEBUG_MARKET_INDEX) === "true";
    const allSectors = common_vendor.computed(() => sectorPayload.value.all || []);
    const topList = common_vendor.computed(() => {
      var _a;
      const source = ((_a = sectorPayload.value.top) == null ? void 0 : _a.length) ? sectorPayload.value.top : allSectors.value;
      return [...source].sort((a, b) => Number(b.rate || 0) - Number(a.rate || 0));
    });
    const bottomList = common_vendor.computed(() => {
      var _a;
      const source = ((_a = sectorPayload.value.bottom) == null ? void 0 : _a.length) ? sectorPayload.value.bottom : allSectors.value;
      return [...source].sort((a, b) => Number(a.rate || 0) - Number(b.rate || 0));
    });
    const flowRows = common_vendor.computed(() => flowPayload.value.rows || []);
    const inflowList = common_vendor.computed(() => {
      var _a;
      const source = ((_a = flowPayload.value.inflow) == null ? void 0 : _a.length) ? flowPayload.value.inflow : flowRows.value;
      return services_api_sector.filterIndustryCapitalFlowRows(source).sort((a, b) => Number(b.mainNet || 0) - Number(a.mainNet || 0));
    });
    const outflowList = common_vendor.computed(() => {
      var _a;
      const source = ((_a = flowPayload.value.outflow) == null ? void 0 : _a.length) ? flowPayload.value.outflow : flowRows.value;
      return services_api_sector.filterIndustryCapitalFlowRows(source).sort((a, b) => Number(a.mainNet || 0) - Number(b.mainNet || 0));
    });
    const sectorCount = common_vendor.computed(() => allSectors.value.length || topList.value.length + bottomList.value.length);
    const updatedAtText = common_vendor.computed(() => sectorPayload.value.updatedAt || "板块与资金流同步观察");
    const visibleIndices = common_vendor.computed(() => indices.value.filter(hasIndexEntry));
    const indexGroups = common_vendor.computed(() => {
      const groups = [
        { key: "cn", title: "A股指数", items: [] },
        { key: "hk", title: "港股指数", items: [] },
        { key: "us", title: "美股指数", items: [] },
        { key: "other", title: "其他指数", items: [] }
      ];
      for (const item of visibleIndices.value) {
        const type = indexType(item);
        const group = groups.find((row) => row.key === type) || groups[0];
        group.items.push(item);
      }
      return groups.filter((group) => group.items.length > 0);
    });
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      loadData(false).catch((error) => console.warn("[sector:load]", error));
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        await loadData(true);
      } catch (error) {
        console.warn("[sector:pull-down-refresh]", error);
        common_vendor.index.showToast({ title: "刷新失败，请稍后重试", icon: "none" });
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadData(force) {
      if (loading.value)
        return;
      const hasPageData = allSectors.value.length > 0 || flowRows.value.length > 0 || indices.value.length > 0;
      if (!force && hasPageData && Date.now() - loadedAt.value < PAGE_CACHE_TTL)
        return;
      loading.value = true;
      try {
        const [sectorsResult, flowResult, indicesResult] = await Promise.allSettled([
          services_api_sector.getSectors(force),
          services_api_sector.getCapitalFlow(force, 100),
          services_api_sector.getGlobalIndices(force)
        ]);
        if (sectorsResult.status === "fulfilled") {
          sectorPayload.value = sectorsResult.value || {};
        } else {
          console.warn("[sector:sectors]", sectorsResult.reason);
        }
        if (flowResult.status === "fulfilled") {
          flowPayload.value = flowResult.value || {};
        } else {
          console.warn("[sector:capital-flow]", flowResult.reason);
        }
        if (indicesResult.status === "fulfilled") {
          indices.value = Array.isArray(indicesResult.value) ? indicesResult.value : [];
        } else {
          console.warn("[sector:global-indices]", indicesResult.reason);
        }
        loadedAt.value = Date.now();
        logGlobalIndicesAudit(indices.value);
      } finally {
        loading.value = false;
      }
    }
    function signedOptionalPercent(value) {
      if (value === null || value === void 0 || value === "")
        return "--";
      return utils_format.signedPercent(value);
    }
    function signedOptionalMoney(value) {
      if (value === null || value === void 0 || value === "")
        return "--";
      return utils_format.signedMoney(value);
    }
    function numericOrDash(value) {
      const n = Number(value);
      return Number.isFinite(n) ? n.toFixed(2) : "--";
    }
    function sectorKey(item, index, prefix) {
      return `${prefix}-${item.key || item.name || "sector"}-${index}`;
    }
    function flowKey(item, index, prefix) {
      return `${prefix}-${item.code || item.name || "flow"}-${index}`;
    }
    function indexKey(item, index) {
      return `${item.name || "index"}-${index}`;
    }
    function openIndexDetail(item) {
      const indexName = encodeURIComponent(String(item.name || ""));
      const indexCode = encodeURIComponent(String(item.code || ""));
      common_vendor.index.navigateTo({ url: `/pages/index-detail/index?indexName=${indexName}&indexCode=${indexCode}` });
    }
    function hasIndexEntry(item) {
      const name = String(item.name || "");
      return Boolean(cleanIndexName(name));
    }
    function indexType(item) {
      const source = item;
      const marketText = `${source.market || source.type || source.category || ""}`.toUpperCase();
      if (/港|HK|HONG/.test(marketText))
        return "hk";
      if (/美|US|USA|NASDAQ|NYSE/.test(marketText))
        return "us";
      if (/A股|沪|深|CN|CHINA|大陆/.test(marketText))
        return "cn";
      const text = `${item.name || ""} ${item.code || ""}`.toUpperCase();
      if (/恒生|HSI|HSTECH|港股|香港/.test(text))
        return "hk";
      if (/纳斯达克|标普|道琼斯|NDX|IXIC|SPX|DJIA|NASDAQ|S&P/.test(text))
        return "us";
      if (/上证|科创|创业板|沪深|中证|000001|000688|399006/.test(text))
        return "cn";
      return "other";
    }
    function cleanIndexName(value) {
      return String(value || "").replace(/\s*\((?:无数据|异常:.*)\)\s*$/g, "").trim();
    }
    function displayIndexName(item) {
      return cleanIndexName(item.name) || "未知指数";
    }
    function indexHasMarketData(item) {
      const name = String(item.name || "");
      return Boolean(cleanIndexName(name)) && !/异常/.test(name) && (indexPointValue(item) !== null || indexRateValue(item.todayRate, item) !== null || indexYearRateValue(item) !== null);
    }
    function indexPointText(item) {
      const value = indexPointValue(item);
      return value !== null ? numericOrDash(value) : "--";
    }
    function indexRateValue(value, item) {
      const source = item;
      const n = firstNumber(
        value,
        source.rate,
        source.changePercent,
        source.pct,
        source.pctChg
      );
      return n;
    }
    function sectorFundCount(item) {
      const explicitCount = firstNumber(item.fundCount);
      if (explicitCount !== null)
        return explicitCount;
      const source = item;
      const legacyCount = firstNumber(source["quotedCount"]);
      return legacyCount ?? 0;
    }
    function indexPercentText(value, item) {
      const n = indexRateValue(value, item);
      return n === null ? "--" : signedOptionalPercent(n);
    }
    function indexYearPercentText(item) {
      const n = indexYearRateValue(item);
      return n === null ? "--" : signedOptionalPercent(n);
    }
    function indexPointValue(item) {
      const source = item;
      return firstPositiveNumber(
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
      return firstNumber(source.yearRate, source.oneYearRate, source.annualRate, source.yearChangePercent);
    }
    function indexHistoryCount(item) {
      const source = item;
      const rows = firstArray(source.klines, source.lines, source.history, source.series, source.data, source.items, source.list);
      return rows.length;
    }
    function firstNumber(...values) {
      for (const value of values) {
        if (value === null || value === void 0)
          continue;
        if (typeof value === "string" && value.trim() === "")
          continue;
        const n = Number(value);
        if (Number.isFinite(n))
          return n;
      }
      return null;
    }
    function firstPositiveNumber(...values) {
      for (const value of values) {
        const n = firstNumber(value);
        if (n !== null && n > 0)
          return n;
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
    function logGlobalIndicesAudit(rows) {
      if (!DEBUG_FIELD_AUDIT)
        return;
      rows.forEach((item) => {
        console.warn("[global.indices fields]", {
          name: item.name,
          code: item.code,
          point: indexPointValue(item),
          todayRate: indexRateValue(item.todayRate, item),
          yearRate: indexYearRateValue(item),
          historyCount: indexHistoryCount(item)
        });
        if (!indexHasMarketData(item)) {
          console.warn("待核实：后端未返回该指数有效行情字段。", {
            name: item.name,
            code: item.code,
            point: indexPointValue(item),
            todayRate: indexRateValue(item.todayRate, item),
            yearRate: indexYearRateValue(item),
            rawPoint: item.point,
            rawLatest: item.latest
          });
        }
      });
    }
    return (_ctx, _cache) => {
      var _a, _b, _c;
      return common_vendor.e({
        a: common_vendor.t(updatedAtText.value),
        b: common_vendor.t(sectorCount.value),
        c: common_vendor.t(((_a = topList.value[0]) == null ? void 0 : _a.name) || "暂无板块数据"),
        d: common_vendor.t(sectorPayload.value.source || "板块基金池 · 实时估值均值"),
        e: common_vendor.t(signedOptionalPercent((_b = topList.value[0]) == null ? void 0 : _b.rate)),
        f: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)((_c = topList.value[0]) == null ? void 0 : _c.rate)),
        g: common_vendor.t(topList.value.length),
        h: topList.value.length === 0
      }, topList.value.length === 0 ? {} : {}, {
        i: common_vendor.f(topList.value.slice(0, 8), (item, index, i0) => {
          return {
            a: common_vendor.t(index + 1),
            b: common_vendor.t(item.name || "未知板块"),
            c: common_vendor.t(sectorFundCount(item)),
            d: common_vendor.t(signedOptionalPercent(item.rate)),
            e: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(item.rate)),
            f: sectorKey(item, index, "top")
          };
        }),
        j: common_vendor.t(bottomList.value.length),
        k: bottomList.value.length === 0
      }, bottomList.value.length === 0 ? {} : {}, {
        l: common_vendor.f(bottomList.value.slice(0, 8), (item, index, i0) => {
          return {
            a: common_vendor.t(index + 1),
            b: common_vendor.t(item.name || "未知板块"),
            c: common_vendor.t(sectorFundCount(item)),
            d: common_vendor.t(signedOptionalPercent(item.rate)),
            e: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(item.rate)),
            f: sectorKey(item, index, "bottom")
          };
        }),
        m: common_vendor.t(flowPayload.value.updatedAt || ""),
        n: inflowList.value.length === 0
      }, inflowList.value.length === 0 ? {} : {}, {
        o: common_vendor.f(inflowList.value.slice(0, 8), (item, index, i0) => {
          return {
            a: common_vendor.t(index + 1),
            b: common_vendor.t(item.name || "未知行业"),
            c: common_vendor.t(signedOptionalPercent(item.mainRatio)),
            d: common_vendor.t(item.mainNetText || signedOptionalMoney(item.mainNet)),
            e: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(item.mainNet)),
            f: common_vendor.t(signedOptionalPercent(item.rate)),
            g: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(item.rate)),
            h: flowKey(item, index, "in")
          };
        }),
        p: common_vendor.t(flowPayload.value.source || ""),
        q: outflowList.value.length === 0
      }, outflowList.value.length === 0 ? {} : {}, {
        r: common_vendor.f(outflowList.value.slice(0, 8), (item, index, i0) => {
          return {
            a: common_vendor.t(index + 1),
            b: common_vendor.t(item.name || "未知行业"),
            c: common_vendor.t(signedOptionalPercent(item.mainRatio)),
            d: common_vendor.t(item.mainNetText || signedOptionalMoney(item.mainNet)),
            e: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(item.mainNet)),
            f: common_vendor.t(signedOptionalPercent(item.rate)),
            g: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(item.rate)),
            h: flowKey(item, index, "out")
          };
        }),
        s: visibleIndices.value.length === 0 && !loading.value
      }, visibleIndices.value.length === 0 && !loading.value ? {
        t: common_vendor.o(($event) => loadData(true), "8d")
      } : {}, {
        v: common_vendor.f(indexGroups.value, (group, k0, i0) => {
          return {
            a: common_vendor.t(group.title),
            b: common_vendor.f(group.items, (item, index, i1) => {
              return common_vendor.e({
                a: common_vendor.t(displayIndexName(item)),
                b: common_vendor.t(indexPointText(item)),
                c: item.updatedAt
              }, item.updatedAt ? {
                d: common_vendor.t(item.updatedAt)
              } : {}, {
                e: !indexHasMarketData(item)
              }, !indexHasMarketData(item) ? {} : {}, {
                f: common_vendor.t(indexPercentText(item.todayRate, item)),
                g: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(indexRateValue(item.todayRate, item))),
                h: common_vendor.t(indexYearPercentText(item)),
                i: common_vendor.n(common_vendor.unref(utils_format.optionalProfitClass)(indexYearRateValue(item))),
                j: indexKey(item, index),
                k: common_vendor.o(($event) => openIndexDetail(item), indexKey(item, index))
              });
            }),
            c: group.key
          };
        }),
        w: common_vendor.p({
          active: "sector"
        }),
        x: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-4c2bd136"]]);
wx.createPage(MiniProgramPage);
