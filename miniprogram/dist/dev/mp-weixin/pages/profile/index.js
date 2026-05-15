"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_auth = require("../../services/api/auth.js");
const stores_session = require("../../stores/session.js");
const stores_theme = require("../../stores/theme.js");
const utils_format = require("../../utils/format.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const avatarUploading = common_vendor.ref(false);
    const avatarUrl = common_vendor.computed(() => stores_session.sessionState.avatarDataUrl || stores_session.sessionState.avatarUrl || "");
    const avatarText = common_vendor.computed(() => utils_format.avatarInitial(stores_session.sessionState.username));
    const displayUsername = common_vendor.computed(() => stores_session.sessionState.displayName || stores_session.sessionState.username || "未登录");
    const profileSubtitle = common_vendor.computed(
      () => stores_session.sessionState.username ? "点击头像或按钮可更换头像" : "登录后可同步你的个人持仓记录。"
    );
    const primaryActionText = common_vendor.computed(() => {
      if (!stores_session.sessionState.username)
        return "登录 / 同步持仓";
      return avatarUploading.value ? "上传中..." : "更换头像";
    });
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      stores_session.loadSession();
    });
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
    async function changeAvatar() {
      if (!stores_session.sessionState.username) {
        common_vendor.index.showToast({ title: "登录后可使用该功能", icon: "none" });
        return;
      }
      if (avatarUploading.value)
        return;
      try {
        const filePath = await chooseImage();
        if (!filePath)
          return;
        avatarUploading.value = true;
        common_vendor.index.showLoading({ title: "上传头像", mask: true });
        const result = await services_api_auth.uploadAvatar(stores_session.sessionState.username, filePath);
        const avatar = result.avatarDataUrl || result.avatarUrl || "";
        if (!avatar)
          throw new Error("头像上传成功但未返回头像数据");
        stores_session.saveSession({
          username: stores_session.sessionState.username,
          displayName: stores_session.sessionState.displayName || stores_session.sessionState.username,
          avatarDataUrl: result.avatarDataUrl || avatar,
          avatarUrl: result.avatarUrl || "",
          loginTime: stores_session.sessionState.loginTime || Date.now()
        });
        common_vendor.index.showToast({ title: "头像已更新", icon: "none" });
      } catch (error) {
        console.warn("[profile:avatar-upload]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "头像上传失败"), icon: "none" });
      } finally {
        avatarUploading.value = false;
        common_vendor.index.hideLoading();
      }
    }
    function handleProfileAction() {
      if (!stores_session.sessionState.username) {
        common_vendor.index.navigateTo({
          url: "/pages/login/index",
          fail: () => common_vendor.index.redirectTo({ url: "/pages/login/index" })
        });
        return;
      }
      changeAvatar();
    }
    function logout() {
      stores_session.clearSession();
      common_vendor.index.reLaunch({ url: "/pages/login/index" });
    }
    function selectTheme(theme) {
      var _a;
      stores_theme.setTheme(theme);
      common_vendor.index.showToast({
        title: ((_a = stores_theme.themeOptions.find((item) => item.value === theme)) == null ? void 0 : _a.label) || "主题已切换",
        icon: "none"
      });
    }
    function goBack() {
      common_vendor.index.navigateBack({
        fail: () => common_vendor.index.reLaunch({ url: "/pages/home/index" })
      });
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
        a: common_vendor.o(goBack, "41"),
        b: avatarUrl.value
      }, avatarUrl.value ? {
        c: avatarUrl.value
      } : {
        d: common_vendor.t(avatarText.value)
      }, {
        e: avatarUploading.value,
        f: common_vendor.o(changeAvatar, "fe"),
        g: common_vendor.t(displayUsername.value),
        h: common_vendor.t(profileSubtitle.value),
        i: common_vendor.t(primaryActionText.value),
        j: avatarUploading.value,
        k: common_vendor.o(handleProfileAction, "ab"),
        l: common_vendor.f(common_vendor.unref(stores_theme.themeOptions), (item, k0, i0) => {
          return {
            a: common_vendor.t(item.label),
            b: common_vendor.t(item.description),
            c: item.value,
            d: common_vendor.n(common_vendor.unref(stores_theme.themeState).theme === item.value ? "active" : ""),
            e: common_vendor.o(($event) => selectTheme(item.value), item.value)
          };
        }),
        m: common_vendor.unref(stores_session.sessionState).username
      }, common_vendor.unref(stores_session.sessionState).username ? {
        n: common_vendor.o(logout, "08")
      } : {}, {
        o: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-f97f9319"]]);
wx.createPage(MiniProgramPage);
