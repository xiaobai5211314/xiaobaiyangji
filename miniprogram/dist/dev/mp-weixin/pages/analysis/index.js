"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_analysis = require("../../services/api/analysis.js");
const stores_session = require("../../stores/session.js");
const stores_theme = require("../../stores/theme.js");
if (!Math) {
  AppTabBar();
}
const AppTabBar = () => "../../components/AppTabBar.js";
const PAGE_CACHE_TTL = 3e4;
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const weekDays = ["一", "二", "三", "四", "五", "六", "日"];
    const loading = common_vendor.ref(false);
    const dashboard = common_vendor.ref({});
    const archives = common_vendor.ref([]);
    const selectedDate = common_vendor.ref(todayDate());
    const currentMonth = common_vendor.ref(todayDate().slice(0, 7));
    const viewMode = common_vendor.ref("amount");
    const loadedAt = common_vendor.ref(0);
    const isGuest = common_vendor.computed(() => !stores_session.sessionState.username);
    const fundRows = common_vendor.computed(
      () => archives.value.filter((row) => !isTotalRow(row)).map((row, index) => normalizeDetailRow(row, index)).filter((row) => Boolean(row.date))
    );
    const totalRows = common_vendor.computed(() => archives.value.filter(isTotalRow));
    const selectedDayRows = common_vendor.computed(() => fundRows.value.filter((row) => row.date === selectedDate.value));
    const selectedTotal = common_vendor.computed(() => totalRows.value.find((row) => normalizeDate(row.recordDate) === selectedDate.value) || null);
    const leadingBlanks = common_vendor.computed(() => {
      const [year, month] = currentMonth.value.split("-").map((item) => Number(item));
      const first = new Date(year, month - 1, 1).getDay();
      const count = first === 0 ? 6 : first - 1;
      return Array.from({ length: count }, (_, index) => `blank-${currentMonth.value}-${index}`);
    });
    const totalByDate = common_vendor.computed(() => {
      const map = /* @__PURE__ */ new Map();
      for (const row of totalRows.value) {
        const date = normalizeDate(row.recordDate);
        if (date)
          map.set(date, row);
      }
      return map;
    });
    const calendarDays = common_vendor.computed(() => {
      const [year, month] = currentMonth.value.split("-").map((item) => Number(item));
      const lastDay = new Date(year, month, 0).getDate();
      return Array.from({ length: lastDay }, (_, index) => {
        const day = index + 1;
        const date = `${currentMonth.value}-${String(day).padStart(2, "0")}`;
        const row = totalByDate.value.get(date);
        const profit = viewMode.value === "rate" ? finiteNumber(row == null ? void 0 : row.dailyRate) : finiteNumber(row == null ? void 0 : row.dailyProfit);
        return {
          key: `day-${date}`,
          date,
          dayText: String(day),
          selected: date === selectedDate.value,
          hasData: Boolean(row),
          profitText: row ? viewMode.value === "rate" ? signedPercentDash(profit) : signedMoneyDash(profit) : "--",
          profitClass: profitClass(profit)
        };
      });
    });
    const overview = common_vendor.computed(() => {
      const dailyReport = dashboard.value.dailyReport || {};
      const total = selectedTotal.value;
      const totalAssets = firstFinite(total == null ? void 0 : total.assets, dailyReport.totalAssets);
      const dailyProfit = firstFinite(total == null ? void 0 : total.dailyProfit, dailyReport.dailyProfit);
      const dailyRate = firstFinite(total == null ? void 0 : total.dailyRate, dailyReport.dailyRate);
      const totalProfit = firstFinite(total == null ? void 0 : total.totalProfit, dailyReport.totalProfit);
      const totalRate = firstFinite(total == null ? void 0 : total.totalRate);
      const fundCount = firstFinite(dailyReport.fundCount, selectedDayRows.value.length);
      return {
        dateText: `${selectedDate.value} · 下拉刷新`,
        statusText: selectedTotal.value ? "已归档收盘数据" : "暂无当日总计",
        fundCountText: fundCount === null ? "--" : String(fundCount),
        totalAssetsText: moneyDash(totalAssets),
        dailyProfitText: signedMoneyDash(dailyProfit),
        dailyProfitClass: profitClass(dailyProfit),
        dailyRateText: signedPercentDash(dailyRate),
        dailyRateClass: profitClass(dailyRate),
        totalProfitText: signedMoneyDash(totalProfit),
        totalProfitClass: profitClass(totalProfit),
        totalRateText: signedPercentDash(totalRate),
        totalRateClass: profitClass(totalRate),
        primaryText: viewMode.value === "rate" ? signedPercentDash(dailyRate) : signedMoneyDash(dailyProfit),
        primaryClass: viewMode.value === "rate" ? profitClass(dailyRate) : profitClass(dailyProfit)
      };
    });
    const profitTop = common_vendor.computed(
      () => dedupeByFund(selectedDayRows.value).filter((row) => rankValue(row) !== null && Number(rankValue(row)) > 0).sort((a, b) => Number(rankValue(b)) - Number(rankValue(a))).slice(0, 5)
    );
    const lossTop = common_vendor.computed(
      () => dedupeByFund(selectedDayRows.value).filter((row) => rankValue(row) !== null && Number(rankValue(row)) < 0).sort((a, b) => Number(rankValue(a)) - Number(rankValue(b))).slice(0, 5)
    );
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      stores_session.loadSession();
      if (!stores_session.sessionState.username) {
        dashboard.value = {};
        archives.value = [];
        return;
      }
      loadData(false).catch((error) => console.warn("[analysis:load]", error));
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        await loadData(true);
      } catch (error) {
        console.warn("[analysis:pull-down-refresh]", error);
        common_vendor.index.showToast({ title: "刷新失败，请稍后重试", icon: "none" });
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadData(force = false) {
      if (loading.value)
        return;
      if (!stores_session.sessionState.username) {
        dashboard.value = {};
        archives.value = [];
        loadedAt.value = 0;
        return;
      }
      if (!force && archives.value.length > 0 && Date.now() - loadedAt.value < PAGE_CACHE_TTL)
        return;
      loading.value = true;
      try {
        const [insightsResult, archivesResult] = await Promise.allSettled([
          services_api_analysis.getInsightsDashboard(stores_session.sessionState.username),
          services_api_analysis.getArchives(stores_session.sessionState.username, 500)
        ]);
        if (insightsResult.status === "fulfilled") {
          dashboard.value = insightsResult.value || {};
        } else {
          console.warn("[analysis:dashboard]", insightsResult.reason);
        }
        if (archivesResult.status === "fulfilled") {
          archives.value = Array.isArray(archivesResult.value) ? archivesResult.value : [];
        } else {
          console.warn("[analysis:archives]", archivesResult.reason);
        }
        loadedAt.value = Date.now();
        if (force || archives.value.length > 0)
          ensureSelectedDate();
      } finally {
        loading.value = false;
      }
    }
    function ensureSelectedDate() {
      const dates = Array.from(totalByDate.value.keys()).sort((a, b) => b.localeCompare(a));
      const today = todayDate();
      const next = dates.includes(today) ? today : dates[0] || today;
      selectedDate.value = next;
      currentMonth.value = next.slice(0, 7);
    }
    function selectDate(date) {
      selectedDate.value = date;
    }
    function setViewMode(mode) {
      viewMode.value = mode;
    }
    function goPrevMonth() {
      shiftMonth(-1);
    }
    function goNextMonth() {
      shiftMonth(1);
    }
    function goToday() {
      const today = todayDate();
      selectedDate.value = today;
      currentMonth.value = today.slice(0, 7);
    }
    function shiftMonth(delta) {
      const [year, month] = currentMonth.value.split("-").map((item) => Number(item));
      const next = new Date(year, month - 1 + delta, 1);
      currentMonth.value = `${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, "0")}`;
    }
    function todayDate() {
      const now = /* @__PURE__ */ new Date();
      return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
    }
    function normalizeDate(value) {
      return value ? String(value).slice(0, 10) : "";
    }
    function isTotalRow(row) {
      return String(row.fundCode || "").toUpperCase() === "TOTAL";
    }
    function normalizeDetailRow(row, index) {
      const source = row;
      const date = normalizeDate(row.recordDate);
      const code = String(row.fundCode || source.code || source.symbol || "").trim();
      const name = String(row.fundName || source.name || "").trim();
      const keyBase = code || name || "fund";
      const dailyProfit = finiteNumber(row.dailyProfit);
      const dailyRate = finiteNumber(row.dailyRate);
      const totalProfit = finiteNumber(row.totalProfit);
      const totalRate = finiteNumber(row.totalRate);
      return {
        key: `${keyBase}-${date}-${index}`,
        date,
        codeKey: keyBase,
        nameText: name || code || "--",
        codeText: code || "--",
        dailyProfit,
        dailyProfitText: signedMoneyDash(dailyProfit),
        dailyProfitClass: profitClass(dailyProfit),
        dailyRate,
        dailyRateText: signedPercentDash(dailyRate),
        dailyRateClass: profitClass(dailyRate),
        totalProfit,
        totalRate
      };
    }
    function dedupeByFund(rows) {
      const map = /* @__PURE__ */ new Map();
      for (const row of rows) {
        const key = row.codeKey || row.nameText;
        const prev = map.get(key);
        if (!prev) {
          map.set(key, row);
          continue;
        }
        const prevRank = rankValue(prev);
        const nextRank = rankValue(row);
        const prevValue = prevRank === null ? -Infinity : Math.abs(prevRank);
        const nextValue = nextRank === null ? -Infinity : Math.abs(nextRank);
        if (nextValue > prevValue)
          map.set(key, row);
      }
      return Array.from(map.values());
    }
    function rankValue(row) {
      return viewMode.value === "rate" ? row.dailyRate : row.dailyProfit;
    }
    function rankText(row) {
      return viewMode.value === "rate" ? row.dailyRateText : row.dailyProfitText;
    }
    function rankClass(row) {
      return viewMode.value === "rate" ? row.dailyRateClass : row.dailyProfitClass;
    }
    function finiteNumber(value) {
      const n = Number(value);
      return Number.isFinite(n) ? n : null;
    }
    function firstFinite(...values) {
      for (const value of values) {
        const n = finiteNumber(value);
        if (n !== null)
          return n;
      }
      return null;
    }
    function moneyDash(value) {
      return value === null ? "--" : `¥ ${value.toFixed(2)}`;
    }
    function signedMoneyDash(value) {
      if (value === null)
        return "--";
      return `${value >= 0 ? "+" : "-"}¥ ${Math.abs(value).toFixed(2)}`;
    }
    function signedPercentDash(value) {
      if (value === null)
        return "--";
      return `${value >= 0 ? "+" : ""}${value.toFixed(2)}%`;
    }
    function profitClass(value) {
      if (value === null)
        return "";
      return value >= 0 ? "profit-text" : "loss-text";
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: common_vendor.t(overview.value.dateText),
        b: common_vendor.t(overview.value.fundCountText),
        c: isGuest.value
      }, isGuest.value ? {} : {}, {
        d: common_vendor.n(viewMode.value === "amount" ? "active" : ""),
        e: common_vendor.o(($event) => setViewMode("amount"), "48"),
        f: common_vendor.n(viewMode.value === "rate" ? "active" : ""),
        g: common_vendor.o(($event) => setViewMode("rate"), "6e"),
        h: common_vendor.t(overview.value.statusText),
        i: common_vendor.t(overview.value.primaryText),
        j: common_vendor.n(overview.value.primaryClass),
        k: common_vendor.t(overview.value.totalAssetsText),
        l: common_vendor.t(viewMode.value === "rate" ? "今日收益率" : "今日收益"),
        m: common_vendor.t(viewMode.value === "rate" ? overview.value.dailyRateText : overview.value.dailyProfitText),
        n: common_vendor.n(viewMode.value === "rate" ? overview.value.dailyRateClass : overview.value.dailyProfitClass),
        o: common_vendor.t(viewMode.value === "rate" ? "累计收益率" : "累计盈亏"),
        p: common_vendor.t(viewMode.value === "rate" ? overview.value.totalRateText : overview.value.totalProfitText),
        q: common_vendor.n(viewMode.value === "rate" ? overview.value.totalRateClass : overview.value.totalProfitClass),
        r: common_vendor.t(viewMode.value === "rate" ? "累计盈亏" : "累计收益率"),
        s: common_vendor.t(viewMode.value === "rate" ? overview.value.totalProfitText : overview.value.totalRateText),
        t: common_vendor.n(viewMode.value === "rate" ? overview.value.totalProfitClass : overview.value.totalRateClass),
        v: common_vendor.o(goPrevMonth, "8c"),
        w: common_vendor.t(currentMonth.value),
        x: common_vendor.o(goNextMonth, "dc"),
        y: common_vendor.o(goToday, "0c"),
        z: common_vendor.f(weekDays, (item, k0, i0) => {
          return {
            a: common_vendor.t(item),
            b: item
          };
        }),
        A: common_vendor.f(leadingBlanks.value, (blank, k0, i0) => {
          return {
            a: blank
          };
        }),
        B: common_vendor.f(calendarDays.value, (day, k0, i0) => {
          return {
            a: common_vendor.t(day.dayText),
            b: common_vendor.t(day.profitText),
            c: common_vendor.n(day.profitClass),
            d: day.key,
            e: common_vendor.n(day.selected ? "selected" : ""),
            f: common_vendor.n(day.hasData ? "" : "empty-day"),
            g: common_vendor.o(($event) => selectDate(day.date), day.key)
          };
        }),
        C: common_vendor.t(viewMode.value === "rate" ? "收益率 TOP5" : "盈利 TOP5"),
        D: profitTop.value.length === 0
      }, profitTop.value.length === 0 ? {} : {}, {
        E: common_vendor.f(profitTop.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.nameText),
            b: common_vendor.t(item.codeText),
            c: common_vendor.t(rankText(item)),
            d: item.key
          };
        }),
        F: common_vendor.t(viewMode.value === "rate" ? "亏损率 TOP5" : "亏损 TOP5"),
        G: lossTop.value.length === 0
      }, lossTop.value.length === 0 ? {} : {}, {
        H: common_vendor.f(lossTop.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.nameText),
            b: common_vendor.t(item.codeText),
            c: common_vendor.t(rankText(item)),
            d: item.key
          };
        }),
        I: common_vendor.t(selectedDate.value),
        J: common_vendor.t(selectedDayRows.value.length),
        K: selectedDayRows.value.length === 0
      }, selectedDayRows.value.length === 0 ? {
        L: common_vendor.t(isGuest.value ? "登录后可同步你的个人持仓记录。" : "该日暂无基金明细，点击重试或下拉刷新"),
        M: common_vendor.o(($event) => !isGuest.value && loadData(true), "30")
      } : {}, {
        N: common_vendor.f(selectedDayRows.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.nameText),
            b: common_vendor.t(item.codeText),
            c: common_vendor.t(rankText(item)),
            d: common_vendor.n(rankClass(item)),
            e: common_vendor.t(viewMode.value === "rate" ? item.dailyProfitText : item.dailyRateText),
            f: item.key
          };
        }),
        O: common_vendor.p({
          active: "analysis"
        }),
        P: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-c6aadd33"]]);
wx.createPage(MiniProgramPage);
