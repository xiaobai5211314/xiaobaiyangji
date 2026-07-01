"use strict";
const common_vendor = require("../../common/vendor.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "webview",
  setup(__props) {
    const sourceUrl = common_vendor.ref("");
    common_vendor.onLoad((options) => {
      const candidate = decodeURIComponent(String((options == null ? void 0 : options.url) || ""));
      if (/^https:\/\/(x\.com|twitter\.com)\//i.test(candidate)) {
        sourceUrl.value = candidate;
      }
    });
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: sourceUrl.value
      }, sourceUrl.value ? {
        b: sourceUrl.value
      } : {});
    };
  }
});
wx.createPage(_sfc_main);
