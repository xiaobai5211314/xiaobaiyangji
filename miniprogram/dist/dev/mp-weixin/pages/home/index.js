"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_auth = require("../../services/api/auth.js");
const services_api_fund = require("../../services/api/fund.js");
const services_api_stock = require("../../services/api/stock.js");
const stores_session = require("../../stores/session.js");
const utils_format = require("../../utils/format.js");
const utils_storage = require("../../utils/storage.js");
const utils_fundMetrics = require("../../utils/fundMetrics.js");
if (!Math) {
  (SparklineChart + AppTabBar)();
}
const AppTabBar = () => "../../components/AppTabBar.js";
const SparklineChart = () => "../../components/SparklineChart.js";
const PRIVACY_KEY = "privacy_mode";
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const rawFunds = common_vendor.ref([]);
    const assetMode = common_vendor.ref("fund");
    const loading = common_vendor.ref(false);
    const profileLoading = common_vendor.ref(false);
    const ocrBusy = common_vendor.ref(false);
    const ocrConfirming = common_vendor.ref(false);
    const ocrPreviewVisible = common_vendor.ref(false);
    const ocrItems = common_vendor.ref([]);
    const ocrDiagnostics = common_vendor.ref([]);
    const privacyMode = common_vendor.ref(normalizePrivacyMode(utils_storage.getStorage(PRIVACY_KEY, 2)));
    const stockLoading = common_vendor.ref(false);
    const stockSearchLoading = common_vendor.ref(false);
    const stockSearchKeyword = common_vendor.ref("");
    const stockSearchResults = common_vendor.ref([]);
    const stockHoldings = common_vendor.ref([]);
    const stockWatchList = common_vendor.ref([]);
    const stockUpdatedAt = common_vendor.ref("");
    const stockKlinePeriod = common_vendor.ref("minute");
    const stockKlineRows = common_vendor.ref([]);
    const selectedStock = common_vendor.ref({});
    const stockOcrPreviewVisible = common_vendor.ref(false);
    const stockOcrConfirming = common_vendor.ref(false);
    const stockOcrBatchId = common_vendor.ref(null);
    const stockOcrItems = common_vendor.ref([]);
    const stockOcrDiagnostics = common_vendor.ref([]);
    const holdingEditor = common_vendor.ref({
      show: false,
      code: "",
      market: "",
      name: "",
      shares: "",
      costPrice: ""
    });
    const historyModal = common_vendor.ref({
      show: false,
      loading: false,
      code: "",
      name: "",
      rows: []
    });
    const metrics = common_vendor.computed(() => utils_fundMetrics.buildPortfolioMetrics(rawFunds.value));
    const funds = common_vendor.computed(() => metrics.value.funds);
    const stockKlinePeriods = [
      { label: "分K", value: "minute" },
      { label: "时K", value: "hour" },
      { label: "日K", value: "day" },
      { label: "月K", value: "month" },
      { label: "年K", value: "year" }
    ];
    const stockOcrActionLabels = ["持仓", "自选"];
    const confidenceRows = common_vendor.computed(
      () => [...funds.value].sort((a, b) => a.confidenceView.score - b.confidenceView.score).slice(0, 4)
    );
    const headerCountText = common_vendor.computed(
      () => assetMode.value === "stock" ? `${stockHoldings.value.length} 持有 · ${stockWatchList.value.length} 自选` : `${funds.value.length} 只基金`
    );
    const ocrButtonText = common_vendor.computed(() => {
      if (ocrBusy.value)
        return assetMode.value === "stock" ? "股票解析中..." : "基金解析中...";
      return assetMode.value === "stock" ? "智能截图导入股票" : "智能截图导入基金";
    });
    const avatarUrl = common_vendor.computed(() => stores_session.sessionState.avatarDataUrl || stores_session.sessionState.avatarUrl || "");
    const avatarText = common_vendor.computed(() => utils_format.avatarInitial(stores_session.sessionState.username));
    const stockKlinePoints = common_vendor.computed(
      () => normalizeStockKlines(stockKlineRows.value).map((row) => row.close)
    );
    const historyRows = common_vendor.computed(
      () => [...historyModal.value.rows].sort((a, b) => String(b.recordDate || "").localeCompare(String(a.recordDate || "")))
    );
    const historyProfitPoints = common_vendor.computed(() => [...historyRows.value].reverse().map((row) => Number(row.dailyProfit || 0)));
    const historyRatePoints = common_vendor.computed(() => [...historyRows.value].reverse().map((row) => Number(row.totalRate ?? row.dailyRate ?? 0)));
    const historyProfitTone = common_vendor.computed(() => {
      const last = historyProfitPoints.value[historyProfitPoints.value.length - 1] || 0;
      return last >= 0 ? "profit" : "loss";
    });
    const privacyLabel = common_vendor.computed(() => {
      const labels = {
        0: "睁眼模式",
        1: "半遮蔽",
        2: "全遮蔽",
        3: "极致隐匿"
      };
      return labels[privacyMode.value];
    });
    common_vendor.onShow(() => {
      stores_session.loadSession();
      privacyMode.value = normalizePrivacyMode(utils_storage.getStorage(PRIVACY_KEY, privacyMode.value));
      if (!stores_session.sessionState.username) {
        common_vendor.index.reLaunch({ url: "/pages/login/index" });
        return;
      }
      loadProfile().catch((error) => console.error("[home:profile]", error));
      if (assetMode.value === "stock") {
        loadStocks(false).catch((error) => console.error("[stock:load]", error));
      } else {
        loadFunds(false).catch((error) => console.error("[home:load]", error));
      }
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        if (assetMode.value === "stock") {
          await loadStocks(true);
          if (selectedStock.value.code || selectedStock.value.stockCode || selectedStock.value.symbol) {
            await loadStockKlines(false);
          }
        } else {
          await loadFunds(true);
        }
      } catch (error) {
        console.error("[home:pull-down-refresh]", error);
        common_vendor.index.showToast({ title: "刷新失败，请稍后重试", icon: "none" });
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadFunds(force) {
      if (!stores_session.sessionState.username || loading.value)
        return;
      loading.value = true;
      try {
        const data = await services_api_fund.getTodayFunds(stores_session.sessionState.username, force);
        const items = Array.isArray(data) ? data : [];
        logFundTodayAudit(items);
        rawFunds.value = items;
      } finally {
        loading.value = false;
      }
    }
    function logFundTodayAudit(items, phase = "today") {
      items.slice(0, 8).forEach((fund) => {
        const fields = getFundNavFields(fund);
        console.log("[fund.today keys]", fund.name, fund.code, Object.keys(fund));
        console.log("[fund.today nav fields]", {
          phase,
          name: fund.name,
          code: fund.code,
          nav: fields.nav,
          estimate: fields.estimate,
          estimateRate: fields.estimateRate,
          deviation: fields.deviation
        });
      });
    }
    function setAssetMode(mode) {
      if (assetMode.value === mode)
        return;
      assetMode.value = mode;
      if (mode === "stock") {
        loadStocks(false).catch((error) => console.error("[stock:load]", error));
      }
    }
    function handleSmartOcr() {
      if (assetMode.value === "stock") {
        startStockOcr();
        return;
      }
      startFundOcr();
    }
    async function loadStocks(force) {
      if (!stores_session.sessionState.username || stockLoading.value)
        return;
      stockLoading.value = true;
      try {
        const data = await services_api_stock.getStockDashboard(stores_session.sessionState.username);
        if (data.success === false)
          throw new Error(String(data.message || "股票数据读取失败"));
        stockHoldings.value = Array.isArray(data.holdings) ? data.holdings : [];
        stockWatchList.value = Array.isArray(data.watchList) ? data.watchList : [];
        stockUpdatedAt.value = String(data.updatedAt || "");
        if (!selectedStock.value.code && !selectedStock.value.stockCode && !selectedStock.value.symbol) {
          const first = stockHoldings.value[0] || stockWatchList.value[0];
          if (first)
            await openStockTrend(first, false);
        }
        if (force)
          common_vendor.index.showToast({ title: "股票数据已刷新", icon: "none" });
      } finally {
        stockLoading.value = false;
      }
    }
    async function handleStockSearch() {
      const keyword = stockSearchKeyword.value.trim();
      if (!keyword) {
        common_vendor.index.showToast({ title: "请输入股票代码或名称", icon: "none" });
        return;
      }
      stockSearchLoading.value = true;
      try {
        const data = await services_api_stock.searchStocks(keyword);
        if (data.success === false)
          throw new Error(String(data.message || "查询失败"));
        stockSearchResults.value = Array.isArray(data.items) ? data.items : [];
        if (!stockSearchResults.value.length) {
          common_vendor.index.showToast({ title: "没有匹配股票", icon: "none" });
        }
      } catch (error) {
        console.error("[stock:search]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "查询失败，请稍后重试"), icon: "none" });
      } finally {
        stockSearchLoading.value = false;
      }
    }
    async function addWatchFromStock(item) {
      const payload = buildStockWatchPayload(item);
      if (!payload)
        return;
      try {
        const result = await services_api_stock.saveStockWatch(payload);
        if (result.success === false)
          throw new Error(String(result.message || "加入自选失败"));
        common_vendor.index.showToast({ title: "已加入自选", icon: "none" });
        await loadStocks(false);
      } catch (error) {
        console.error("[stock:add-watch]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "加入自选失败"), icon: "none" });
      }
    }
    function openHoldingEditor(item) {
      const code = stockCode(item);
      if (!code) {
        common_vendor.index.showToast({ title: "股票代码缺失", icon: "none" });
        return;
      }
      const shares = stockShares(item);
      const price = firstKnown(stockPickNumber(item, "costPrice"), stockPrice(item));
      holdingEditor.value = {
        show: true,
        code,
        market: stockMarket(item),
        name: stockName(item),
        shares: shares === null ? "" : String(shares),
        costPrice: price === null ? "" : String(price)
      };
    }
    function closeHoldingEditor() {
      holdingEditor.value.show = false;
    }
    async function submitHoldingEditor() {
      if (!stores_session.sessionState.username)
        return;
      const shares = toFiniteNumber(holdingEditor.value.shares);
      const costPrice = toFiniteNumber(holdingEditor.value.costPrice);
      if (!holdingEditor.value.code || shares === null || shares <= 0 || costPrice === null || costPrice <= 0) {
        common_vendor.index.showToast({ title: "请填写有效持股数量和成本价", icon: "none" });
        return;
      }
      try {
        const result = await services_api_stock.saveStockHolding({
          username: stores_session.sessionState.username,
          stockCode: holdingEditor.value.code,
          stockName: holdingEditor.value.name,
          market: holdingEditor.value.market,
          shares,
          costPrice,
          costAmount: Number((shares * costPrice).toFixed(2))
        });
        if (result.success === false)
          throw new Error(String(result.message || "保存持仓失败"));
        closeHoldingEditor();
        common_vendor.index.showToast({ title: "股票持仓已保存", icon: "none" });
        await loadStocks(false);
      } catch (error) {
        console.error("[stock:save-holding]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "保存持仓失败"), icon: "none" });
      }
    }
    async function removeHolding(item) {
      if (!stores_session.sessionState.username)
        return;
      const code = stockCode(item);
      if (!code)
        return;
      const confirmed = await confirmModal(`删除股票持仓：${stockName(item)}？`);
      if (!confirmed)
        return;
      try {
        const result = await services_api_stock.deleteStockHolding(stores_session.sessionState.username, code, stockMarket(item));
        if (result.success === false)
          throw new Error(String(result.message || "删除持仓失败"));
        common_vendor.index.showToast({ title: "已删除股票持仓", icon: "none" });
        await loadStocks(false);
      } catch (error) {
        console.error("[stock:delete-holding]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "删除持仓失败"), icon: "none" });
      }
    }
    async function removeWatch(item) {
      if (!stores_session.sessionState.username)
        return;
      const code = stockCode(item);
      if (!code)
        return;
      try {
        const result = await services_api_stock.deleteStockWatch(stores_session.sessionState.username, code, stockMarket(item));
        if (result.success === false)
          throw new Error(String(result.message || "移除自选失败"));
        common_vendor.index.showToast({ title: "已移除自选", icon: "none" });
        await loadStocks(false);
      } catch (error) {
        console.error("[stock:delete-watch]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "移除自选失败"), icon: "none" });
      }
    }
    async function openStockTrend(item, showLoading = true) {
      const code = stockCode(item);
      if (!code) {
        common_vendor.index.showToast({ title: "股票代码缺失", icon: "none" });
        return;
      }
      selectedStock.value = {
        ...item,
        code,
        market: stockMarket(item),
        name: stockName(item)
      };
      await loadStockKlines(showLoading);
    }
    async function switchStockKline(period) {
      if (stockKlinePeriod.value === period)
        return;
      stockKlinePeriod.value = period;
      await loadStockKlines(true);
    }
    async function loadStockKlines(showError = true) {
      const code = stockCode(selectedStock.value);
      if (!code) {
        stockKlineRows.value = [];
        return;
      }
      try {
        const data = await services_api_stock.getStockKlines(code, stockKlinePeriod.value);
        if (data.success === false)
          throw new Error(String(data.message || "走势读取失败"));
        stockKlineRows.value = Array.isArray(data.items) ? normalizeStockKlines(data.items) : [];
      } catch (error) {
        console.error("[stock:klines]", error);
        stockKlineRows.value = [];
        if (showError)
          common_vendor.index.showToast({ title: getErrorMessage(error, "走势读取失败"), icon: "none" });
      }
    }
    async function startStockOcr() {
      if (!stores_session.sessionState.username || ocrBusy.value)
        return;
      try {
        const filePath = await chooseImage();
        if (!filePath)
          return;
        ocrBusy.value = true;
        common_vendor.index.showLoading({ title: "股票解析中", mask: true });
        const result = await services_api_stock.previewStockOcr(stores_session.sessionState.username, filePath);
        if (result.success === false)
          throw new Error(String(result.message || "股票 OCR 失败"));
        stockOcrBatchId.value = Number(result.batchId || 0) || null;
        stockOcrItems.value = (Array.isArray(result.items) ? result.items : []).map(normalizeStockOcrPreviewItem);
        stockOcrDiagnostics.value = Array.isArray(result.diagnostics) ? result.diagnostics : [];
        stockOcrPreviewVisible.value = true;
        common_vendor.index.showToast({ title: `识别到 ${stockOcrItems.value.length} 条股票`, icon: "none" });
      } catch (error) {
        console.error("[stock:ocr-preview]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "股票 OCR 失败"), icon: "none" });
      } finally {
        ocrBusy.value = false;
        common_vendor.index.hideLoading();
      }
    }
    async function confirmStockOcrImport() {
      if (!stores_session.sessionState.username || stockOcrConfirming.value)
        return;
      if (!stockOcrBatchId.value || stockOcrItems.value.length === 0)
        return;
      stockOcrConfirming.value = true;
      try {
        const result = await services_api_stock.confirmStockOcr({
          username: stores_session.sessionState.username,
          batchId: stockOcrBatchId.value,
          items: stockOcrItems.value
        });
        if (result.success === false)
          throw new Error(String(result.message || "股票 OCR 确认失败"));
        closeStockOcrPreview();
        common_vendor.index.showToast({ title: `已写入 ${result.saved ?? 0} 条股票数据`, icon: "none" });
        await loadStocks(false);
      } catch (error) {
        console.error("[stock:ocr-confirm]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "股票 OCR 确认失败"), icon: "none" });
      } finally {
        stockOcrConfirming.value = false;
      }
    }
    function closeStockOcrPreview() {
      stockOcrPreviewVisible.value = false;
    }
    function normalizeStockOcrPreviewItem(item) {
      const shares = toFiniteNumber(item.shares);
      const costPrice = toFiniteNumber(item.costPrice);
      const costAmount = toFiniteNumber(item.costAmount);
      const marketValue = toFiniteNumber(item.marketValue);
      const normalized = {
        ...item,
        action: item.action || "holding",
        shares,
        costPrice,
        costAmount,
        marketValue,
        floatingProfit: toFiniteNumber(item.floatingProfit),
        floatingProfitRate: toFiniteNumber(item.floatingProfitRate)
      };
      return recalculateStockOcrItem(normalized);
    }
    function recalculateStockOcrItem(item) {
      const shares = toFiniteNumber(item.shares);
      const costPrice = toFiniteNumber(item.costPrice);
      if (shares !== null && costPrice !== null) {
        item.costAmount = Number((shares * costPrice).toFixed(2));
      }
      const costAmount = toFiniteNumber(item.costAmount);
      const marketValue = toFiniteNumber(item.marketValue);
      if (marketValue !== null && costAmount !== null) {
        item.floatingProfit = Number((marketValue - costAmount).toFixed(2));
        item.floatingProfitRate = costAmount > 0 ? Number((Number(item.floatingProfit) / costAmount * 100).toFixed(4)) : item.floatingProfitRate;
      }
      return item;
    }
    function stockOcrActionIndex(item) {
      return item.action === "watch" ? 1 : 0;
    }
    function updateStockOcrAction(index, event) {
      const next = [...stockOcrItems.value];
      const item = next[index];
      if (!item)
        return;
      item.action = Number(pickEventValue(event)) === 1 ? "watch" : "holding";
      stockOcrItems.value = next;
    }
    function updateStockOcrNumber(index, key, event) {
      const next = [...stockOcrItems.value];
      const item = next[index];
      if (!item)
        return;
      item[key] = toFiniteNumber(pickEventValue(event));
      next[index] = recalculateStockOcrItem(item);
      stockOcrItems.value = next;
    }
    function pickEventValue(event) {
      var _a;
      if (event && typeof event === "object" && "detail" in event) {
        return (_a = event.detail) == null ? void 0 : _a.value;
      }
      return void 0;
    }
    function stockOcrInputText(value) {
      const n = toFiniteNumber(value);
      return n === null ? "" : String(n);
    }
    function stockOcrItemKey(item, index) {
      return `${item.id || item.stockCode || item.stockName || item.recognizedName || "stock-ocr"}-${index}`;
    }
    function buildStockWatchPayload(item) {
      if (!stores_session.sessionState.username)
        return null;
      const code = stockCode(item);
      if (!code) {
        common_vendor.index.showToast({ title: "股票代码缺失", icon: "none" });
        return null;
      }
      return {
        username: stores_session.sessionState.username,
        stockCode: code,
        stockName: stockName(item),
        market: stockMarket(item)
      };
    }
    function normalizeStockKlines(rows) {
      return rows.map((row, index) => ({
        time: String(firstKnown(row.time, row.date, row.datetime, row.tradeTime, index) || ""),
        timeOrder: parsePointTime(firstKnown(row.time, row.date, row.datetime, row.tradeTime), index),
        open: stockPickNumber(row, "open") ?? stockPickNumber(row, "close") ?? 0,
        close: stockPickNumber(row, "close") ?? stockPickNumber(row, "price") ?? stockPickNumber(row, "current") ?? 0,
        high: stockPickNumber(row, "high") ?? stockPickNumber(row, "highest") ?? stockPickNumber(row, "close") ?? 0,
        low: stockPickNumber(row, "low") ?? stockPickNumber(row, "lowest") ?? stockPickNumber(row, "close") ?? 0,
        volume: stockPickNumber(row, "volume") ?? void 0,
        amount: stockPickNumber(row, "amount") ?? void 0,
        changeRate: stockPickNumber(row, "changeRate") ?? stockPickNumber(row, "rate") ?? void 0
      })).filter((row) => Number.isFinite(row.close)).sort((a, b) => a.timeOrder - b.timeOrder);
    }
    function stockName(item) {
      return String(firstKnown(item.name, item.stockName, item.securityName, stockCode(item), "未命名股票") || "未命名股票");
    }
    function stockCode(item) {
      return String(firstKnown(item.code, item.stockCode, item.symbol, "") || "");
    }
    function stockMarket(item) {
      return String(firstKnown(item.market, item.exchange, item.type, "--") || "--");
    }
    function stockPrice(item) {
      return firstKnownNumber(item.price, item.current, item.latest, item.last, item.close);
    }
    function stockRate(item) {
      return stockRateValue(item) ?? 0;
    }
    function stockRateValue(item) {
      return firstKnownNumber(item.changeRate, item.rate, item.pct, item.percent, item.changePercent);
    }
    function stockShares(item) {
      return firstKnownNumber(
        item.shares,
        item.amount,
        item.quantity,
        item.count,
        item.holdAmount
      );
    }
    function stockMarketValue(item) {
      return firstKnownNumber(
        item.marketValue,
        item.value,
        item.totalValue
      );
    }
    function stockProfit(item) {
      return firstKnownNumber(
        item.totalProfit,
        item.profit,
        item.holdingProfit,
        item.income,
        item.gain
      );
    }
    function stockPickNumber(item, ...keys) {
      const values = keys.map((key) => item[key]);
      return firstKnownNumber(...values);
    }
    function firstKnownNumber(...values) {
      for (const value of values) {
        const n = toFiniteNumber(value);
        if (n !== null)
          return n;
      }
      return null;
    }
    function toFiniteNumber(value) {
      if (value === null || value === void 0 || value === "")
        return null;
      const n = Number(String(value).replace(/,/g, "").replace("%", ""));
      return Number.isFinite(n) ? n : null;
    }
    function stockPriceText(item) {
      const value = stockPrice(item);
      return value === null ? "--" : value.toFixed(value >= 100 ? 2 : 3);
    }
    function stockRateText(item) {
      const value = stockRateValue(item);
      return value === null ? "--" : utils_format.signedPercent(value);
    }
    function stockPercentText(value) {
      const n = toFiniteNumber(value);
      return n === null ? "--" : utils_format.signedPercent(n);
    }
    function stockSharesText(item) {
      const value = stockShares(item);
      return value === null ? "--" : value.toFixed(value % 1 === 0 ? 0 : 2);
    }
    function stockMoneyText(value) {
      const n = toFiniteNumber(value);
      return n === null ? "--" : utils_fundMetrics.maskByPrivacy(utils_format.formatMoney(n), privacyMode.value, 1);
    }
    function stockSignedMoneyText(value) {
      const n = toFiniteNumber(value);
      return n === null ? "--" : utils_fundMetrics.maskByPrivacy(utils_format.signedMoney(n), privacyMode.value, 1);
    }
    function stockItemKey(item, scope) {
      return `${scope}-${stockMarket(item)}-${stockCode(item)}-${String(item.id || "")}`;
    }
    function confirmModal(content) {
      return new Promise((resolve) => {
        common_vendor.index.showModal({
          title: "确认操作",
          content,
          success: (result) => resolve(!!result.confirm),
          fail: () => resolve(false)
        });
      });
    }
    async function loadProfile() {
      if (!stores_session.sessionState.username || profileLoading.value)
        return;
      profileLoading.value = true;
      try {
        const profile = await services_api_auth.getProfile(stores_session.sessionState.username);
        const username = services_api_auth.pickUsername(profile, stores_session.sessionState.username);
        const avatar = services_api_auth.pickAvatar(profile) || stores_session.sessionState.avatarDataUrl || stores_session.sessionState.avatarUrl;
        stores_session.saveSession({
          username,
          displayName: username,
          avatarDataUrl: avatar,
          loginTime: stores_session.sessionState.loginTime || Date.now()
        });
      } finally {
        profileLoading.value = false;
      }
    }
    function openProfile() {
      common_vendor.index.navigateTo({ url: "/pages/profile/index" });
    }
    function normalizePrivacyMode(value) {
      const n = Number(value);
      return n === 0 || n === 1 || n === 2 || n === 3 ? n : 2;
    }
    function togglePrivacyMode() {
      const next = (privacyMode.value + 1) % 4;
      privacyMode.value = next;
      utils_storage.setStorage(PRIVACY_KEY, next);
    }
    function displayMoney(value, requiredMode, sign) {
      return utils_fundMetrics.maskByPrivacy(sign ? utils_format.signedMoney(value) : utils_format.formatMoney(value), privacyMode.value, requiredMode);
    }
    function archiveMoney(value) {
      const n = Number(value);
      return Number.isFinite(n) ? utils_format.formatMoney(n) : "--";
    }
    function displayPercent(value, requiredMode) {
      return utils_fundMetrics.maskByPrivacy(utils_format.signedPercent(value), privacyMode.value, requiredMode);
    }
    function signedPlainPercent(value) {
      return utils_format.signedPercent(value);
    }
    function displayFundCost(fund) {
      if (privacyMode.value !== 0)
        return "****";
      return fund.costValue && fund.costValue > 0 ? utils_format.formatMoney(fund.costValue) : "未设置";
    }
    function numericOrDash(value, digits = 2) {
      const n = Number(value);
      return Number.isFinite(n) ? n.toFixed(digits) : "--";
    }
    function fundNavLabel(fund) {
      const fields = getFundNavFields(fund);
      if (fields.nav !== null && fields.estimate !== null)
        return "净值/估值";
      if (fields.nav !== null)
        return "净值";
      if (fields.estimate !== null)
        return "估值";
      return "净值/估值";
    }
    function fundNavText(fund) {
      const fields = getFundNavFields(fund);
      const navText = fields.nav === null ? "" : fields.nav.toFixed(4);
      const estimateText = fields.estimate === null ? "" : fields.estimate.toFixed(4);
      if (navText && estimateText)
        return `${navText} / ${estimateText}`;
      if (navText)
        return navText;
      if (estimateText)
        return estimateText;
      return "--";
    }
    function fundDeviationValue(fund) {
      return getFundNavFields(fund).deviation;
    }
    function fundDeviationText(fund) {
      const value = fundDeviationValue(fund);
      return value === null ? "--" : utils_format.signedPercent(value);
    }
    function getFundNavFields(fund) {
      const source = fund;
      const nav = firstKnownNavNumber(
        source.todayNav,
        source.TodayNav,
        source.nav,
        source.Nav,
        source.netValue,
        source.NetValue,
        source.latestNav,
        source.latestNetValue,
        source.unitNetValue,
        source.dwjz,
        source.DWJZ,
        source.actualNav,
        source.actualNetValue
      );
      const estimate = firstKnownNavNumber(
        source.estimate,
        source.valuation,
        source.estimatedNav,
        source.estimateNav,
        source.valuationValue,
        source.currentEstimate,
        source.gsz,
        source.GSZ
      );
      const estimateRate = firstKnownPercentNumber(
        source.estimateRate,
        source.valuationRate,
        source.gszzl,
        source.GSZZL,
        source.estimatedRate
      );
      const deviation = firstKnownPercentNumber(
        source.deviation,
        source.estimateDeviation,
        source.valuationDeviation,
        source.navDeviation,
        source.diffRate,
        source.DiffRate,
        source.premiumRate
      );
      return { nav, estimate, estimateRate, deviation };
    }
    function firstKnownNavNumber(...values) {
      for (const value of values) {
        const n = toFiniteNumber(value);
        if (n !== null)
          return n;
      }
      return null;
    }
    function firstKnownPercentNumber(...values) {
      for (const value of values) {
        const n = toFiniteNumber(value);
        if (n !== null)
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
    function confidenceToneClass(tone) {
      if (tone === "high")
        return "confidence-high";
      if (tone === "medium")
        return "confidence-medium";
      return "confidence-low";
    }
    function todayTrendPoints(fund) {
      const rows = Array.isArray(fund.data) ? fund.data : [];
      const points = rows.map((point, index) => {
        if (Array.isArray(point)) {
          return {
            time: parsePointTime(point[0], index),
            value: Number(point[1])
          };
        }
        if (point && typeof point === "object") {
          const row = point;
          return {
            time: parsePointTime(firstKnown(row.time, row.date, row.datetime, row.day), index),
            value: Number(firstKnown(row.rate, row.currentRate, row.value, row.close, row.gsz, row.gszzl))
          };
        }
        return {
          time: index,
          value: Number(point)
        };
      }).filter((point) => Number.isFinite(point.value)).sort((a, b) => a.time - b.time).map((point) => point.value);
      return points.length > 1 ? points : [];
    }
    function trendPointCount(fund) {
      return todayTrendPoints(fund).length;
    }
    function fundTrendCanvasId(fund) {
      const raw = `${fund.viewKey || fund.code || "fund"}`;
      const safe = raw.replace(/[^a-zA-Z0-9_-]/g, "-");
      return `fund-trend-${safe}`;
    }
    function parsePointTime(value, fallback) {
      if (value instanceof Date)
        return value.getTime();
      const text = String(value || "").replace(/\//g, "-");
      const time = Date.parse(text);
      return Number.isFinite(time) ? time : fallback;
    }
    function chooseImage() {
      return new Promise((resolve, reject) => {
        common_vendor.index.chooseImage({
          count: 1,
          sourceType: ["album", "camera"],
          success: (result) => {
            var _a;
            return resolve(((_a = result.tempFilePaths) == null ? void 0 : _a[0]) || null);
          },
          fail: (error) => {
            const message = String((error == null ? void 0 : error.errMsg) || "");
            if (/cancel/i.test(message)) {
              resolve(null);
              return;
            }
            reject(error);
          }
        });
      });
    }
    async function startFundOcr() {
      if (!stores_session.sessionState.username || ocrBusy.value)
        return;
      try {
        const filePath = await chooseImage();
        if (!filePath)
          return;
        ocrBusy.value = true;
        common_vendor.index.showLoading({ title: "基金解析中", mask: true });
        const result = await services_api_fund.previewFundOcr(stores_session.sessionState.username, filePath);
        if (result.success === false) {
          throw new Error(String(result.message || "OCR 识别失败"));
        }
        ocrItems.value = Array.isArray(result.items) ? result.items : [];
        ocrDiagnostics.value = Array.isArray(result.diagnostics) ? result.diagnostics : [];
        ocrPreviewVisible.value = true;
        common_vendor.index.showToast({ title: `识别到 ${ocrItems.value.length} 条基金`, icon: "none" });
      } catch (error) {
        console.error("[fund:ocr-preview]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "OCR 识别失败"), icon: "none" });
      } finally {
        ocrBusy.value = false;
        common_vendor.index.hideLoading();
      }
    }
    async function confirmOcrImport() {
      if (!stores_session.sessionState.username || ocrConfirming.value || ocrItems.value.length === 0)
        return;
      ocrConfirming.value = true;
      try {
        const result = await services_api_fund.confirmFundOcr({
          username: stores_session.sessionState.username,
          items: ocrItems.value
        });
        if (result.success === false) {
          throw new Error(String(result.message || "OCR 导入失败"));
        }
        common_vendor.index.showToast({ title: result.message || "导入成功", icon: "none" });
        closeOcrPreview();
        await loadFunds(true);
      } catch (error) {
        console.error("[fund:ocr-confirm]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "OCR 导入失败"), icon: "none" });
      } finally {
        ocrConfirming.value = false;
      }
    }
    function closeOcrPreview() {
      ocrPreviewVisible.value = false;
    }
    async function openFundHistory(fund) {
      if (!stores_session.sessionState.username || !fund.code) {
        common_vendor.index.showToast({ title: "基金代码缺失，无法读取历史", icon: "none" });
        return;
      }
      historyModal.value = {
        show: true,
        loading: true,
        code: fund.code,
        name: fund.name || fund.code,
        rows: []
      };
      try {
        const rows = await services_api_fund.getFundArchives(stores_session.sessionState.username, fund.code, 365);
        historyModal.value.rows = Array.isArray(rows) ? rows : [];
      } catch (error) {
        console.error("[fund:history]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "历史读取失败"), icon: "none" });
      } finally {
        historyModal.value.loading = false;
      }
    }
    function closeFundHistory() {
      historyModal.value.show = false;
    }
    function formatDate(value) {
      if (!value)
        return "--";
      return String(value).slice(0, 10);
    }
    function historyRowKey(row, index) {
      return `${row.fundCode || historyModal.value.code || "fund"}-${formatDate(row.recordDate)}-${index}`;
    }
    function ocrPick(item, camelKey, pascalKey) {
      return item[camelKey] ?? item[pascalKey];
    }
    function ocrText(item, camelKey, pascalKey) {
      const value = ocrPick(item, camelKey, pascalKey);
      return value === null || value === void 0 ? "" : String(value);
    }
    function ocrNumber(item, camelKey, pascalKey) {
      return utils_fundMetrics.moneyDash(ocrPick(item, camelKey, pascalKey), false);
    }
    function ocrItemKey(item, index) {
      return `${ocrText(item, "code", "Code") || ocrText(item, "name", "Name") || "ocr"}-${index}`;
    }
    function getErrorMessage(error, fallback) {
      if (error instanceof Error && error.message)
        return error.message;
      if (error && typeof error === "object" && "errMsg" in error) {
        return String(error.errMsg || fallback);
      }
      return fallback;
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: common_vendor.t(headerCountText.value),
        b: common_vendor.t(ocrButtonText.value),
        c: ocrBusy.value,
        d: common_vendor.o(handleSmartOcr, "de"),
        e: avatarUrl.value
      }, avatarUrl.value ? {
        f: avatarUrl.value
      } : {
        g: common_vendor.t(avatarText.value)
      }, {
        h: common_vendor.o(openProfile, "ad"),
        i: common_vendor.t(privacyLabel.value),
        j: common_vendor.o(togglePrivacyMode, "ae"),
        k: common_vendor.n(assetMode.value === "fund" ? "active" : ""),
        l: common_vendor.o(($event) => setAssetMode("fund"), "04"),
        m: common_vendor.n(assetMode.value === "stock" ? "active" : ""),
        n: common_vendor.o(($event) => setAssetMode("stock"), "05"),
        o: assetMode.value === "fund"
      }, assetMode.value === "fund" ? common_vendor.e({
        p: common_vendor.t(funds.value.length),
        q: common_vendor.t(displayMoney(metrics.value.totalAssets, 0, false)),
        r: common_vendor.t(displayMoney(metrics.value.totalCost, 0, false)),
        s: common_vendor.t(displayMoney(metrics.value.totalPrincipal, 0, false)),
        t: common_vendor.t(displayMoney(metrics.value.totalTodayProfit, 1, true)),
        v: common_vendor.n(common_vendor.unref(utils_format.profitClass)(metrics.value.totalTodayProfit)),
        w: common_vendor.t(displayPercent(metrics.value.totalTodayRate, 2)),
        x: common_vendor.n(common_vendor.unref(utils_format.profitClass)(metrics.value.totalTodayRate)),
        y: common_vendor.t(displayMoney(metrics.value.totalProfit, 1, true)),
        z: common_vendor.n(common_vendor.unref(utils_format.profitClass)(metrics.value.totalProfit)),
        A: common_vendor.t(displayPercent(metrics.value.totalRate, 2)),
        B: common_vendor.n(common_vendor.unref(utils_format.profitClass)(metrics.value.totalRate)),
        C: common_vendor.t(metrics.value.dailyBattleReport.summary),
        D: common_vendor.t(common_vendor.unref(utils_fundMetrics.maskByPrivacy)(metrics.value.dailyBattleReport.todayProfitText, privacyMode.value, 1)),
        E: common_vendor.n(common_vendor.unref(utils_format.profitClass)(metrics.value.totalTodayProfit)),
        F: common_vendor.t(metrics.value.dailyBattleReport.bestName),
        G: common_vendor.t(metrics.value.dailyBattleReport.worstName),
        H: common_vendor.t(metrics.value.dailyBattleReport.actionHint),
        I: common_vendor.f(confidenceRows.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.name || item.code),
            b: common_vendor.t(item.confidenceView.reason),
            c: common_vendor.t(item.confidenceView.score),
            d: common_vendor.n(confidenceToneClass(item.confidenceView.tone)),
            e: `conf-${item.viewKey}`
          };
        }),
        J: metrics.value.profitTop.length === 0
      }, metrics.value.profitTop.length === 0 ? {} : {}, {
        K: common_vendor.f(metrics.value.profitTop, (fund, k0, i0) => {
          return {
            a: common_vendor.t(fund.name || fund.code),
            b: common_vendor.t(displayMoney(fund.estimatedProfitValue, 1, true)),
            c: `profit-${fund.viewKey}`
          };
        }),
        L: metrics.value.lossTop.length === 0
      }, metrics.value.lossTop.length === 0 ? {} : {}, {
        M: common_vendor.f(metrics.value.lossTop, (fund, k0, i0) => {
          return {
            a: common_vendor.t(fund.name || fund.code),
            b: common_vendor.t(displayMoney(fund.estimatedProfitValue, 1, true)),
            c: `loss-${fund.viewKey}`
          };
        }),
        N: common_vendor.t(funds.value.length),
        O: funds.value.length === 0 && !loading.value
      }, funds.value.length === 0 && !loading.value ? {} : {}, {
        P: common_vendor.f(funds.value, (fund, k0, i0) => {
          return common_vendor.e({
            a: common_vendor.t(fund.name || "未命名基金"),
            b: common_vendor.t(fund.code || "待更新"),
            c: common_vendor.t(fund.statusLabel),
            d: common_vendor.n(fund.isSettledValue ? "settled" : fund.isHolidayValue ? "holiday" : "tracking"),
            e: common_vendor.t(displayPercent(fund.currentRateValue, 2)),
            f: common_vendor.n(common_vendor.unref(utils_format.profitClass)(fund.currentRateValue)),
            g: fund.calibrationNote || fund.calibrationOffset !== void 0
          }, fund.calibrationNote || fund.calibrationOffset !== void 0 ? {
            h: common_vendor.t(signedPlainPercent(fund.calibrationOffset || 0)),
            i: common_vendor.t(fund.calibrationNote || "滚动误差校准")
          } : {}, {
            j: common_vendor.t(trendPointCount(fund)),
            k: "2c5296db-0-" + i0,
            l: common_vendor.p({
              ["canvas-id"]: fundTrendCanvasId(fund),
              points: todayTrendPoints(fund),
              tone: fund.currentRateValue >= 0 ? "profit" : "loss",
              ["empty-text"]: "暂无估值走势数据"
            }),
            m: common_vendor.t(fundNavLabel(fund)),
            n: common_vendor.t(fundNavText(fund)),
            o: fundDeviationText(fund) !== "--"
          }, fundDeviationText(fund) !== "--" ? {
            p: common_vendor.t(fundDeviationText(fund)),
            q: common_vendor.n(common_vendor.unref(utils_format.profitClass)(fundDeviationValue(fund)))
          } : {
            r: common_vendor.t(fund.trendLabel)
          }, {
            s: common_vendor.t(displayFundCost(fund)),
            t: common_vendor.t(displayMoney(fund.todayAmountValue, 0, false)),
            v: common_vendor.t(displayMoney(fund.todayProfitValue, 1, true)),
            w: common_vendor.n(common_vendor.unref(utils_format.profitClass)(fund.todayProfitValue)),
            x: common_vendor.t(displayPercent(fund.currentRateValue, 2)),
            y: common_vendor.n(common_vendor.unref(utils_format.profitClass)(fund.currentRateValue)),
            z: common_vendor.t(displayMoney(fund.estimatedProfitValue, 1, true)),
            A: common_vendor.n(common_vendor.unref(utils_format.profitClass)(fund.estimatedProfitValue)),
            B: common_vendor.t(displayPercent(fund.existingReturnRateValue, 2)),
            C: common_vendor.n(common_vendor.unref(utils_format.profitClass)(fund.existingReturnRateValue)),
            D: common_vendor.t(numericOrDash(fund.shares, 2)),
            E: common_vendor.t(fund.confidenceView.reason),
            F: common_vendor.o(($event) => openFundHistory(fund), fund.viewKey),
            G: common_vendor.t(fund.confidenceView.level),
            H: common_vendor.t(fund.confidenceView.score),
            I: common_vendor.n(confidenceToneClass(fund.confidenceView.tone)),
            J: fund.viewKey
          });
        }),
        Q: ocrPreviewVisible.value
      }, ocrPreviewVisible.value ? common_vendor.e({
        R: common_vendor.o(closeOcrPreview, "e9"),
        S: ocrItems.value.length === 0
      }, ocrItems.value.length === 0 ? {} : {}, {
        T: common_vendor.f(ocrItems.value, (item, index, i0) => {
          return common_vendor.e({
            a: common_vendor.t(ocrText(item, "name", "Name") || ocrText(item, "ocrName", "OcrName") || "待匹配基金"),
            b: common_vendor.t(ocrText(item, "code", "Code") || "待核对"),
            c: common_vendor.t(ocrNumber(item, "holdAmount", "HoldAmount")),
            d: common_vendor.t(ocrNumber(item, "costAmount", "CostAmount")),
            e: common_vendor.t(ocrNumber(item, "holdingIncome", "HoldingIncome")),
            f: common_vendor.t(ocrNumber(item, "yesterdayIncome", "YesterdayIncome")),
            g: common_vendor.t(common_vendor.unref(utils_fundMetrics.percentDash)(ocrPick(item, "holdingRate", "HoldingRate"))),
            h: common_vendor.t(numericOrDash(ocrPick(item, "matchScore", "MatchScore"), 0)),
            i: ocrText(item, "warning", "Warning")
          }, ocrText(item, "warning", "Warning") ? {
            j: common_vendor.t(ocrText(item, "warning", "Warning"))
          } : {}, {
            k: ocrItemKey(item, index)
          });
        }),
        U: ocrDiagnostics.value.length
      }, ocrDiagnostics.value.length ? {
        V: common_vendor.f(ocrDiagnostics.value, (line, index, i0) => {
          return {
            a: common_vendor.t(line),
            b: `diag-${index}`
          };
        })
      } : {}, {
        W: common_vendor.o(closeOcrPreview, "18"),
        X: common_vendor.t(ocrConfirming.value ? "导入中..." : "确认导入"),
        Y: ocrConfirming.value || ocrItems.value.length === 0,
        Z: common_vendor.o(confirmOcrImport, "47"),
        aa: common_vendor.o(closeOcrPreview, "35")
      }) : {}, {
        ab: historyModal.value.show
      }, historyModal.value.show ? common_vendor.e({
        ac: common_vendor.t(historyModal.value.name),
        ad: common_vendor.o(closeFundHistory, "ba"),
        ae: historyModal.value.loading
      }, historyModal.value.loading ? {} : common_vendor.e({
        af: common_vendor.t(historyRows.value.length),
        ag: common_vendor.p({
          ["canvas-id"]: "fund-history-profit",
          points: historyProfitPoints.value,
          tone: historyProfitTone.value
        }),
        ah: common_vendor.t(historyRows.value.length),
        ai: common_vendor.p({
          ["canvas-id"]: "fund-history-rate",
          points: historyRatePoints.value,
          tone: "neutral"
        }),
        aj: historyRows.value.length === 0
      }, historyRows.value.length === 0 ? {} : {}, {
        ak: common_vendor.f(historyRows.value.slice(0, 80), (row, index, i0) => {
          return {
            a: common_vendor.t(formatDate(row.recordDate)),
            b: common_vendor.t(archiveMoney(row.assets)),
            c: common_vendor.t(archiveMoney(row.cost)),
            d: common_vendor.t(common_vendor.unref(utils_format.signedMoney)(row.dailyProfit || 0)),
            e: common_vendor.n(common_vendor.unref(utils_format.profitClass)(row.dailyProfit)),
            f: common_vendor.t(common_vendor.unref(utils_format.signedPercent)(row.dailyRate || 0)),
            g: common_vendor.n(common_vendor.unref(utils_format.profitClass)(row.dailyRate)),
            h: historyRowKey(row, index)
          };
        })
      }), {
        al: common_vendor.o(closeFundHistory, "56")
      }) : {}) : common_vendor.e({
        am: common_vendor.t(stockUpdatedAt.value || "待刷新"),
        an: common_vendor.o(handleStockSearch, "ae"),
        ao: stockSearchKeyword.value,
        ap: common_vendor.o(($event) => stockSearchKeyword.value = $event.detail.value, "53"),
        aq: common_vendor.t(stockSearchLoading.value ? "查询中" : "查询"),
        ar: stockSearchLoading.value,
        as: common_vendor.o(handleStockSearch, "6b"),
        at: stockSearchResults.value.length
      }, stockSearchResults.value.length ? {
        av: common_vendor.f(stockSearchResults.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(stockName(item)),
            b: common_vendor.t(stockCode(item)),
            c: common_vendor.t(stockMarket(item)),
            d: common_vendor.t(stockPriceText(item)),
            e: common_vendor.t(stockRateText(item)),
            f: common_vendor.n(common_vendor.unref(utils_format.profitClass)(stockRate(item))),
            g: common_vendor.o(($event) => openStockTrend(item), stockItemKey(item, "search")),
            h: common_vendor.o(($event) => addWatchFromStock(item), stockItemKey(item, "search")),
            i: common_vendor.o(($event) => openHoldingEditor(item), stockItemKey(item, "search")),
            j: stockItemKey(item, "search")
          };
        })
      } : {}, {
        aw: selectedStock.value.code
      }, selectedStock.value.code ? {
        ax: common_vendor.t(selectedStock.value.name || selectedStock.value.code),
        ay: common_vendor.t(selectedStock.value.code),
        az: common_vendor.t(selectedStock.value.market || "--"),
        aA: common_vendor.t(stockPriceText(selectedStock.value)),
        aB: common_vendor.t(stockRateText(selectedStock.value)),
        aC: common_vendor.n(common_vendor.unref(utils_format.profitClass)(stockRate(selectedStock.value))),
        aD: common_vendor.f(stockKlinePeriods, (period, k0, i0) => {
          return {
            a: common_vendor.t(period.label),
            b: period.value,
            c: common_vendor.n(stockKlinePeriod.value === period.value ? "active" : ""),
            d: common_vendor.o(($event) => switchStockKline(period.value), period.value)
          };
        }),
        aE: common_vendor.t(stockKlineRows.value.length),
        aF: common_vendor.p({
          ["canvas-id"]: `stock-${selectedStock.value.code}-${stockKlinePeriod.value}`,
          points: stockKlinePoints.value,
          tone: stockRate(selectedStock.value) >= 0 ? "profit" : "loss",
          ["empty-text"]: "暂无走势数据"
        })
      } : {}, {
        aG: common_vendor.t(stockHoldings.value.length),
        aH: stockHoldings.value.length === 0
      }, stockHoldings.value.length === 0 ? {} : {}, {
        aI: common_vendor.f(stockHoldings.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(stockName(item)),
            b: common_vendor.t(stockCode(item)),
            c: common_vendor.t(stockMarket(item)),
            d: common_vendor.t(stockPriceText(item)),
            e: common_vendor.t(stockRateText(item)),
            f: common_vendor.n(common_vendor.unref(utils_format.profitClass)(stockRate(item))),
            g: common_vendor.t(stockSharesText(item)),
            h: common_vendor.t(stockMoneyText(stockMarketValue(item))),
            i: common_vendor.t(stockSignedMoneyText(stockProfit(item))),
            j: common_vendor.n(common_vendor.unref(utils_format.profitClass)(stockProfit(item))),
            k: common_vendor.t(stockPercentText(stockPickNumber(item, "totalProfitRate"))),
            l: common_vendor.n(common_vendor.unref(utils_format.profitClass)(stockPickNumber(item, "totalProfitRate"))),
            m: common_vendor.o(($event) => openStockTrend(item), stockItemKey(item, "holding")),
            n: common_vendor.o(($event) => addWatchFromStock(item), stockItemKey(item, "holding")),
            o: common_vendor.o(($event) => removeHolding(item), stockItemKey(item, "holding")),
            p: stockItemKey(item, "holding")
          };
        }),
        aJ: common_vendor.t(stockWatchList.value.length),
        aK: stockWatchList.value.length === 0
      }, stockWatchList.value.length === 0 ? {} : {}, {
        aL: common_vendor.f(stockWatchList.value, (item, k0, i0) => {
          return {
            a: common_vendor.t(stockName(item)),
            b: common_vendor.t(stockCode(item)),
            c: common_vendor.t(stockMarket(item)),
            d: common_vendor.t(stockPriceText(item)),
            e: common_vendor.t(stockRateText(item)),
            f: common_vendor.n(common_vendor.unref(utils_format.profitClass)(stockRate(item))),
            g: common_vendor.o(($event) => openStockTrend(item), stockItemKey(item, "watch")),
            h: common_vendor.o(($event) => openHoldingEditor(item), stockItemKey(item, "watch")),
            i: common_vendor.o(($event) => removeWatch(item), stockItemKey(item, "watch")),
            j: stockItemKey(item, "watch")
          };
        }),
        aM: stockOcrPreviewVisible.value
      }, stockOcrPreviewVisible.value ? common_vendor.e({
        aN: common_vendor.o(closeStockOcrPreview, "e5"),
        aO: stockOcrItems.value.length === 0
      }, stockOcrItems.value.length === 0 ? {} : {}, {
        aP: common_vendor.f(stockOcrItems.value, (item, index, i0) => {
          return common_vendor.e({
            a: common_vendor.t(item.stockName || item.recognizedName || "待匹配股票"),
            b: common_vendor.t(item.stockCode || "待核对"),
            c: common_vendor.t(item.action === "watch" ? "自选" : "持仓"),
            d: stockOcrActionIndex(item),
            e: common_vendor.o(($event) => updateStockOcrAction(index, $event), stockOcrItemKey(item, index)),
            f: stockOcrInputText(item.shares),
            g: common_vendor.o(($event) => updateStockOcrNumber(index, "shares", $event), stockOcrItemKey(item, index)),
            h: stockOcrInputText(item.costPrice),
            i: common_vendor.o(($event) => updateStockOcrNumber(index, "costPrice", $event), stockOcrItemKey(item, index)),
            j: stockOcrInputText(item.costAmount),
            k: common_vendor.o(($event) => updateStockOcrNumber(index, "costAmount", $event), stockOcrItemKey(item, index)),
            l: common_vendor.t(stockMoneyText(item.marketValue)),
            m: common_vendor.t(stockSignedMoneyText(item.floatingProfit)),
            n: common_vendor.n(common_vendor.unref(utils_format.profitClass)(item.floatingProfit)),
            o: item.note
          }, item.note ? {
            p: common_vendor.t(item.note)
          } : {}, {
            q: stockOcrItemKey(item, index)
          });
        }),
        aQ: stockOcrActionLabels,
        aR: stockOcrDiagnostics.value.length
      }, stockOcrDiagnostics.value.length ? {
        aS: common_vendor.f(stockOcrDiagnostics.value, (line, index, i0) => {
          return {
            a: common_vendor.t(line),
            b: `stock-diag-${index}`
          };
        })
      } : {}, {
        aT: common_vendor.o(closeStockOcrPreview, "af"),
        aU: common_vendor.t(stockOcrConfirming.value ? "导入中..." : "确认导入"),
        aV: stockOcrConfirming.value || stockOcrItems.value.length === 0,
        aW: common_vendor.o(confirmStockOcrImport, "fa"),
        aX: common_vendor.o(closeStockOcrPreview, "d8")
      }) : {}, {
        aY: holdingEditor.value.show
      }, holdingEditor.value.show ? {
        aZ: common_vendor.t(holdingEditor.value.name),
        ba: common_vendor.t(holdingEditor.value.code),
        bb: common_vendor.o(closeHoldingEditor, "9e"),
        bc: holdingEditor.value.shares,
        bd: common_vendor.o(($event) => holdingEditor.value.shares = $event.detail.value, "cc"),
        be: holdingEditor.value.costPrice,
        bf: common_vendor.o(($event) => holdingEditor.value.costPrice = $event.detail.value, "37"),
        bg: common_vendor.o(closeHoldingEditor, "83"),
        bh: common_vendor.o(submitHoldingEditor, "53"),
        bi: common_vendor.o(closeHoldingEditor, "7c")
      } : {}), {
        bj: common_vendor.p({
          active: "home"
        })
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-2c5296db"]]);
wx.createPage(MiniProgramPage);
