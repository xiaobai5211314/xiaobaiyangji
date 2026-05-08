"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_auth = require("../../services/api/auth.js");
const stores_session = require("../../stores/session.js");
const utils_format = require("../../utils/format.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const avatarUploading = common_vendor.ref(false);
    const avatarUrl = common_vendor.computed(() => stores_session.sessionState.avatarDataUrl || stores_session.sessionState.avatarUrl || "");
    const avatarText = common_vendor.computed(() => utils_format.avatarInitial(stores_session.sessionState.username));
    const displayUsername = common_vendor.computed(() => stores_session.sessionState.displayName || stores_session.sessionState.username || "未登录");
    common_vendor.onShow(() => {
      stores_session.loadSession();
      if (!stores_session.sessionState.username) {
        common_vendor.index.reLaunch({ url: "/pages/login/index" });
      }
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
      if (!stores_session.sessionState.username || avatarUploading.value)
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
        console.error("[profile:avatar-upload]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "头像上传失败"), icon: "none" });
      } finally {
        avatarUploading.value = false;
        common_vendor.index.hideLoading();
      }
    }
    function logout() {
      stores_session.clearSession();
      common_vendor.index.reLaunch({ url: "/pages/login/index" });
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
        a: common_vendor.o(goBack, "ec"),
        b: avatarUrl.value
      }, avatarUrl.value ? {
        c: avatarUrl.value
      } : {
        d: common_vendor.t(avatarText.value)
      }, {
        e: avatarUploading.value,
        f: common_vendor.o(changeAvatar, "21"),
        g: common_vendor.t(displayUsername.value),
        h: common_vendor.t(avatarUploading.value ? "上传中..." : "更换头像"),
        i: avatarUploading.value,
        j: common_vendor.o(changeAvatar, "23"),
        k: common_vendor.o(logout, "5b")
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-f97f9319"]]);
wx.createPage(MiniProgramPage);
