"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_analysis = require("../../services/api/analysis.js");
const stores_session = require("../../stores/session.js");
if (!Math) {
  AppTabBar();
}
const AppTabBar = () => "../../components/AppTabBar.js";
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
      stores_session.loadSession();
      if (!stores_session.sessionState.username) {
        common_vendor.index.reLaunch({ url: "/pages/login/index" });
        return;
      }
      loadData().catch((error) => console.error("[analysis:load]", error));
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        await loadData();
      } catch (error) {
        console.error("[analysis:pull-down-refresh]", error);
        common_vendor.index.showToast({ title: "刷新失败，请稍后重试", icon: "none" });
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadData() {
      if (!stores_session.sessionState.username || loading.value)
        return;
      loading.value = true;
      try {
        const [insights, rows] = await Promise.all([services_api_analysis.getInsightsDashboard(stores_session.sessionState.username), services_api_analysis.getArchives(stores_session.sessionState.username, 500)]);
        dashboard.value = insights || {};
        archives.value = Array.isArray(rows) ? rows : [];
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
        c: common_vendor.n(viewMode.value === "amount" ? "active" : ""),
        d: common_vendor.o(($event) => setViewMode("amount"), "48"),
        e: common_vendor.n(viewMode.value === "rate" ? "active" : ""),
        f: common_vendor.o(($event) => setViewMode("rate"), "75"),
        g: common_vendor.t(overview.value.statusText),
        h: common_vendor.t(overview.value.primaryText),
        i: common_vendor.n(overview.value.primaryClass),
        j: common_vendor.t(overview.value.totalAssetsText),
        k: common_vendor.t(viewMode.value === "rate" ? "今日收益率" : "今日收益"),
        l: common_vendor.t(viewMode.value === "rate" ? overview.value.dailyRateText : overview.value.dailyProfitText),
        m: common_vendor.n(viewMode.value === "rate" ? overview.value.dailyRateClass : overview.value.dailyProfitClass),
        n: common_vendor.t(viewMode.value === "rate" ? "累计收益率" : "累计盈亏"),
        o: common_vendor.t(viewMode.value === "rate" ? overview.value.totalRateText : overview.value.totalProfitText),
        p: common_vendor.n(viewMode.value === "rate" ? overview.value.totalRateClass : overview.value.totalProfitClass),
        q: common_vendor.t(viewMode.value === "rate" ? "累计盈亏" : "累计收益率"),
        r: common_vendor.t(viewMode.value === "rate" ? overview.value.totalProfitText : overview.value.totalRateText),
        s: common_vendor.n(viewMode.value === "rate" ? overview.value.totalProfitClass : overview.value.totalRateClass),
        t: common_vendor.o(goPrevMonth, "5c"),
        v: common_vendor.t(currentMonth.value),
        w: common_vendor.o(goNextMonth, "44"),
        x: common_vendor.o(goToday, "1f"),
        y: common_vendor.f(weekDays, (item, k0, i0) => {
          return {
            a: common_vendor.t(item),
            b: item
          };
        }),
        z: common_vendor.f(leadingBlanks.value, (blank, k0, i0) => {
          return {
            a: blank
          };
        }),
        A: common_vendor.f(calendarDays.value, (day, k0, i0) => {
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
        B: common_vendor.t(viewMode.value === "rate" ? "收益率 TOP5" : "盈利 TOP5"),
        C: profitTop.value.length === 0
      }, profitTop.value.length === 0 ? {} : {}, {
        D: common_vendor.f(profitTop.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.nameText),
            b: common_vendor.t(item.codeText),
            c: common_vendor.t(rankText(item)),
            d: item.key
          };
        }),
        E: common_vendor.t(viewMode.value === "rate" ? "亏损率 TOP5" : "亏损 TOP5"),
        F: lossTop.value.length === 0
      }, lossTop.value.length === 0 ? {} : {}, {
        G: common_vendor.f(lossTop.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.nameText),
            b: common_vendor.t(item.codeText),
            c: common_vendor.t(rankText(item)),
            d: item.key
          };
        }),
        H: common_vendor.t(selectedDate.value),
        I: common_vendor.t(selectedDayRows.value.length),
        J: selectedDayRows.value.length === 0
      }, selectedDayRows.value.length === 0 ? {} : {}, {
        K: common_vendor.f(selectedDayRows.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.nameText),
            b: common_vendor.t(item.codeText),
            c: common_vendor.t(rankText(item)),
            d: common_vendor.n(rankClass(item)),
            e: common_vendor.t(viewMode.value === "rate" ? item.dailyProfitText : item.dailyRateText),
            f: item.key
          };
        }),
        L: common_vendor.p({
          active: "analysis"
        })
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-c6aadd33"]]);
wx.createPage(MiniProgramPage);
