"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_auth = require("../../services/api/auth.js");
const stores_session = require("../../stores/session.js");
const stores_theme = require("../../stores/theme.js");
const utils_format = require("../../utils/format.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const profileSaving = common_vendor.ref(false);
    const nicknameDraft = common_vendor.ref("");
    const selectedAvatarPath = common_vendor.ref("");
    const previewAvatarUrl = common_vendor.ref("");
    const avatarUrl = common_vendor.computed(() => previewAvatarUrl.value || stores_session.sessionState.avatarDataUrl || stores_session.sessionState.avatarUrl || "");
    const displayUsername = common_vendor.computed(() => {
      const nickname = String(stores_session.sessionState.displayName || "").trim();
      if (nickname)
        return nickname;
      if (stores_session.sessionState.username && services_api_auth.isGeneratedWechatUsername(stores_session.sessionState.username))
        return "未设置昵称";
      return stores_session.sessionState.username || "未登录";
    });
    const avatarText = common_vendor.computed(() => utils_format.avatarInitial(displayUsername.value === "未设置昵称" ? "估" : displayUsername.value));
    const profileSubtitle = common_vendor.computed(
      () => stores_session.sessionState.username ? "可选择微信头像、填写昵称后保存" : "登录后可同步你的个人持仓记录。"
    );
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      stores_session.loadSession();
      syncDraftFromSession();
    });
    function syncDraftFromSession() {
      nicknameDraft.value = stores_session.sessionState.displayName && !services_api_auth.isGeneratedWechatUsername(stores_session.sessionState.displayName) ? stores_session.sessionState.displayName : "";
      selectedAvatarPath.value = "";
      previewAvatarUrl.value = "";
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
    async function changeAvatar() {
      if (!stores_session.sessionState.username) {
        common_vendor.index.showToast({ title: "登录后可使用该功能", icon: "none" });
        return;
      }
      if (profileSaving.value)
        return;
      try {
        const filePath = await chooseImage();
        if (!filePath)
          return;
        selectedAvatarPath.value = filePath;
        previewAvatarUrl.value = filePath;
        common_vendor.index.showToast({ title: "头像已选择，请保存资料", icon: "none" });
      } catch (error) {
        console.warn("[profile:choose-avatar]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "头像选择失败"), icon: "none" });
      }
    }
    function onChooseAvatar(event) {
      var _a;
      if (!stores_session.sessionState.username) {
        navigateToLogin();
        return;
      }
      const avatarUrl2 = String(((_a = event.detail) == null ? void 0 : _a.avatarUrl) || "").trim();
      if (!avatarUrl2) {
        common_vendor.index.showToast({ title: "未选择头像", icon: "none" });
        return;
      }
      selectedAvatarPath.value = avatarUrl2;
      previewAvatarUrl.value = avatarUrl2;
    }
    function onNicknameInput(event) {
      const detail = event.detail;
      nicknameDraft.value = String((detail == null ? void 0 : detail.value) || "");
    }
    async function saveProfile() {
      if (!stores_session.sessionState.username) {
        common_vendor.index.showToast({ title: "登录后可使用该功能", icon: "none" });
        return;
      }
      if (profileSaving.value)
        return;
      profileSaving.value = true;
      common_vendor.index.showLoading({ title: "保存资料", mask: true });
      try {
        let avatarDataUrl = stores_session.sessionState.avatarDataUrl || "";
        let avatarUrl2 = stores_session.sessionState.avatarUrl || "";
        if (selectedAvatarPath.value) {
          const uploadResult = await services_api_auth.uploadAvatar(stores_session.sessionState.username, selectedAvatarPath.value);
          const uploadedAvatar = uploadResult.avatarDataUrl || uploadResult.avatarUrl || "";
          if (!uploadedAvatar)
            throw new Error("头像上传成功但未返回头像数据");
          avatarDataUrl = uploadResult.avatarDataUrl || uploadedAvatar;
          avatarUrl2 = uploadResult.avatarUrl || "";
        }
        const result = await services_api_auth.updateProfile({
          username: stores_session.sessionState.username,
          displayName: nicknameDraft.value.trim(),
          avatarDataUrl: selectedAvatarPath.value ? avatarDataUrl : void 0
        });
        const nextAvatar = services_api_auth.pickAvatar(result) || avatarDataUrl || avatarUrl2;
        const nextDisplayName = services_api_auth.pickDisplayName(result, stores_session.sessionState.username) || nicknameDraft.value.trim();
        stores_session.saveSession({
          username: stores_session.sessionState.username,
          displayName: nextDisplayName,
          avatarDataUrl: nextAvatar,
          avatarUrl: avatarUrl2,
          loginTime: stores_session.sessionState.loginTime || Date.now()
        });
        selectedAvatarPath.value = "";
        previewAvatarUrl.value = "";
        nicknameDraft.value = nextDisplayName;
        common_vendor.index.showToast({ title: "资料已保存", icon: "none" });
      } catch (error) {
        console.warn("[profile:save]", error);
        common_vendor.index.showToast({ title: getErrorMessage(error, "资料保存失败"), icon: "none" });
      } finally {
        profileSaving.value = false;
        common_vendor.index.hideLoading();
      }
    }
    function navigateToLogin() {
      common_vendor.index.navigateTo({
        url: "/pages/login/index",
        fail: () => common_vendor.index.redirectTo({ url: "/pages/login/index" })
      });
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
        a: common_vendor.o(goBack, "0d"),
        b: common_vendor.unref(stores_session.sessionState).username
      }, common_vendor.unref(stores_session.sessionState).username ? common_vendor.e({
        c: avatarUrl.value
      }, avatarUrl.value ? {
        d: avatarUrl.value
      } : {
        e: common_vendor.t(avatarText.value)
      }, {
        f: profileSaving.value,
        g: common_vendor.o(onChooseAvatar, "30")
      }) : {
        h: common_vendor.t(avatarText.value),
        i: common_vendor.o(navigateToLogin, "e1")
      }, {
        j: common_vendor.t(displayUsername.value),
        k: common_vendor.t(profileSubtitle.value),
        l: common_vendor.unref(stores_session.sessionState).username
      }, common_vendor.unref(stores_session.sessionState).username ? {
        m: nicknameDraft.value,
        n: common_vendor.o(onNicknameInput, "15"),
        o: common_vendor.t(profileSaving.value ? "保存中..." : "保存资料"),
        p: profileSaving.value,
        q: common_vendor.o(saveProfile, "97"),
        r: profileSaving.value,
        s: common_vendor.o(changeAvatar, "5e")
      } : {
        t: common_vendor.o(navigateToLogin, "74")
      }, {
        v: common_vendor.f(common_vendor.unref(stores_theme.themeOptions), (item, k0, i0) => {
          return {
            a: common_vendor.t(item.label),
            b: common_vendor.t(item.description),
            c: item.value,
            d: common_vendor.n(common_vendor.unref(stores_theme.themeState).theme === item.value ? "active" : ""),
            e: common_vendor.o(($event) => selectTheme(item.value), item.value)
          };
        }),
        w: common_vendor.unref(stores_session.sessionState).username
      }, common_vendor.unref(stores_session.sessionState).username ? {
        x: common_vendor.o(logout, "d6")
      } : {}, {
        y: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-f97f9319"]]);
wx.createPage(MiniProgramPage);
