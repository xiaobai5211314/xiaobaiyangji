"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_news = require("../../services/api/news.js");
const stores_session = require("../../stores/session.js");
if (!Math) {
  AppTabBar();
}
const AppTabBar = () => "../../components/AppTabBar.js";
const PAGE_CACHE_TTL = 6e4;
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const loading = common_vendor.ref(false);
    const mode = common_vendor.ref("global");
    const globalPayload = common_vendor.ref({});
    const holdingPayload = common_vendor.ref({});
    const loadedAt = common_vendor.ref(0);
    const activeItems = common_vendor.computed(() => {
      const rows = mode.value === "holding" ? holdingPayload.value.items : globalPayload.value.items;
      return Array.isArray(rows) ? rows : [];
    });
    const updatedAtText = common_vendor.computed(() => {
      const payload = mode.value === "holding" ? holdingPayload.value : globalPayload.value;
      return payload.updatedAt || "市场快讯与持仓影响";
    });
    common_vendor.onShow(() => {
      stores_session.restoreSession();
      loadData(false).catch((error) => console.warn("[news:load]", error));
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        await loadData(true);
      } catch (error) {
        console.warn("[news:pull-down-refresh]", error);
        common_vendor.index.showToast({ title: "刷新失败，请稍后重试", icon: "none" });
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadData(force) {
      if (loading.value)
        return;
      const hasPageData = Array.isArray(globalPayload.value.items) && globalPayload.value.items.length > 0 || Array.isArray(holdingPayload.value.items) && holdingPayload.value.items.length > 0;
      if (!force && hasPageData && Date.now() - loadedAt.value < PAGE_CACHE_TTL)
        return;
      loading.value = true;
      try {
        const [globalResult, holdingResult] = await Promise.allSettled([
          services_api_news.getGlobalNews(force, false, 60),
          stores_session.sessionState.username ? services_api_news.getHoldingNews(stores_session.sessionState.username, force, false, 40) : Promise.resolve({ items: [] })
        ]);
        if (globalResult.status === "fulfilled") {
          globalPayload.value = globalResult.value || {};
        } else {
          console.warn("[news:global]", globalResult.reason);
        }
        if (holdingResult.status === "fulfilled") {
          holdingPayload.value = holdingResult.value || {};
        } else {
          console.warn("[news:holding]", holdingResult.reason);
        }
        loadedAt.value = Date.now();
      } finally {
        loading.value = false;
      }
    }
    function newsItemKey(item, index) {
      return `${item.id || item.title || item.showTime || "news"}-${index}`;
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: common_vendor.t(updatedAtText.value),
        b: common_vendor.t(activeItems.value.length),
        c: common_vendor.n(mode.value === "global" ? "active" : ""),
        d: common_vendor.o(($event) => mode.value = "global", "cb"),
        e: common_vendor.n(mode.value === "holding" ? "active" : ""),
        f: common_vendor.o(($event) => mode.value = "holding", "2f"),
        g: activeItems.value.length === 0 && !loading.value
      }, activeItems.value.length === 0 && !loading.value ? {
        h: common_vendor.t(mode.value === "holding" && !common_vendor.unref(stores_session.sessionState).username ? "登录后可同步你的个人持仓记录。" : "暂无资讯数据，点击重试或下拉刷新"),
        i: common_vendor.o(($event) => loadData(true), "44")
      } : {}, {
        j: common_vendor.f(activeItems.value, (item, itemIndex, i0) => {
          return common_vendor.e({
            a: common_vendor.t(item.timeText || item.dateText || item.showTime || "刚刚"),
            b: common_vendor.t(item.source || "东方财富"),
            c: common_vendor.t(item.title || "暂无标题"),
            d: item.summary
          }, item.summary ? {
            e: common_vendor.t(item.summary)
          } : {}, {
            f: item.important
          }, item.important ? {} : {}, {
            g: item.sentiment
          }, item.sentiment ? {
            h: common_vendor.t(item.sentiment)
          } : {}, {
            i: item.matchedFundName
          }, item.matchedFundName ? {
            j: common_vendor.t(item.matchedFundName)
          } : {}, {
            k: common_vendor.f(item.tags || [], (tag, tagIndex, i1) => {
              return {
                a: common_vendor.t(tag),
                b: `${newsItemKey(item, itemIndex)}-${tag}-${tagIndex}`
              };
            }),
            l: newsItemKey(item, itemIndex)
          });
        }),
        k: common_vendor.p({
          active: "news"
        })
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-a33dc070"]]);
wx.createPage(MiniProgramPage);
