"use strict";
Object.defineProperty(exports, Symbol.toStringTag, { value: "Module" });
const common_vendor = require("./common/vendor.js");
const stores_session = require("./stores/session.js");
const stores_theme = require("./stores/theme.js");
if (!Math) {
  "./pages/home/index.js";
  "./pages/sector/index.js";
  "./pages/news/index.js";
  "./pages/analysis/index.js";
  "./pages/tweets/index.js";
  "./pages/tweets/webview.js";
  "./pages/login/index.js";
  "./pages/profile/index.js";
  "./pages/index-detail/index.js";
}
const _sfc_main = {
  onLaunch() {
    stores_theme.loadTheme();
    stores_session.restoreSession();
  }
};
function createApp() {
  const app = common_vendor.createSSRApp(_sfc_main);
  return {
    app
  };
}
createApp().app.mount("#app");
exports.createApp = createApp;
