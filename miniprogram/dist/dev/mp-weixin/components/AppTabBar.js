"use strict";
const common_vendor = require("../common/vendor.js");
const stores_theme = require("../stores/theme.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "AppTabBar",
  props: {
    active: {}
  },
  setup(__props) {
    const tabs = [
      { key: "home", icon: "🛡️", label: "持仓" },
      { key: "sector", icon: "📈", label: "板块" },
      { key: "news", icon: "📰", label: "资讯" },
      { key: "analysis", icon: "📊", label: "盈亏" }
    ];
    function handleTap(key) {
      const routes = {
        home: "/pages/home/index",
        sector: "/pages/sector/index",
        news: "/pages/news/index",
        analysis: "/pages/analysis/index"
      };
      common_vendor.index.reLaunch({ url: routes[key] });
    }
    return (_ctx, _cache) => {
      return {
        a: common_vendor.f(tabs, (item, k0, i0) => {
          return {
            a: common_vendor.t(item.icon),
            b: common_vendor.t(item.label),
            c: item.key,
            d: common_vendor.n(_ctx.active === item.key ? "active" : ""),
            e: common_vendor.o(($event) => handleTap(item.key), item.key)
          };
        }),
        b: common_vendor.n(common_vendor.unref(stores_theme.themeState).theme === "neon" ? "theme-neon" : "theme-dark")
      };
    };
  }
});
const Component = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-64fe4807"]]);
wx.createComponent(Component);
