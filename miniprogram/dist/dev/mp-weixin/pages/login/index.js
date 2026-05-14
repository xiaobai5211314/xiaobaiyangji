"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_auth = require("../../services/api/auth.js");
const stores_session = require("../../stores/session.js");
const stores_theme = require("../../stores/theme.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const submitting = common_vendor.ref(false);
    const registering = common_vendor.ref(false);
    const errorMessage = common_vendor.ref("");
    const form = common_vendor.reactive({
      username: "",
      password: ""
    });
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      const session = stores_session.loadSession();
      if (session == null ? void 0 : session.username) {
        common_vendor.index.reLaunch({ url: "/pages/home/index" });
      }
    });
    function getErrorMessage(error) {
      if (error instanceof Error && error.message)
        return error.message;
      return "操作失败，请稍后重试";
    }
    function readCredentials() {
      const username = form.username.trim();
      const password = form.password;
      if (!username || !password) {
        errorMessage.value = "请输入账号和密码";
        common_vendor.index.showToast({ title: "请输入账号和密码", icon: "none" });
        return null;
      }
      return { username, password };
    }
    async function submit() {
      const credentials = readCredentials();
      if (!credentials)
        return;
      submitting.value = true;
      errorMessage.value = "";
      try {
        const result = await services_api_auth.login(credentials);
        const username = services_api_auth.pickUsername(result, credentials.username);
        stores_session.saveSession({
          username,
          displayName: services_api_auth.pickDisplayName(result, username),
          avatarDataUrl: services_api_auth.pickAvatar(result),
          loginTime: Date.now()
        });
        form.password = "";
        common_vendor.index.reLaunch({ url: "/pages/home/index" });
      } catch (error) {
        console.warn("[login:submit]", error);
        errorMessage.value = getErrorMessage(error);
      } finally {
        submitting.value = false;
      }
    }
    async function registerAccount() {
      const credentials = readCredentials();
      if (!credentials)
        return;
      registering.value = true;
      errorMessage.value = "";
      try {
        await services_api_auth.register(credentials);
        form.password = "";
        common_vendor.index.showToast({ title: "注册成功，请登录", icon: "none" });
      } catch (error) {
        console.warn("[login:register]", error);
        errorMessage.value = getErrorMessage(error);
      } finally {
        registering.value = false;
      }
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: form.username,
        b: common_vendor.o(common_vendor.m(($event) => form.username = $event.detail.value, {
          trim: true
        }), "70"),
        c: common_vendor.o(submit, "32"),
        d: form.password,
        e: common_vendor.o(($event) => form.password = $event.detail.value, "a1"),
        f: errorMessage.value
      }, errorMessage.value ? {
        g: common_vendor.t(errorMessage.value)
      } : {}, {
        h: common_vendor.t(submitting.value ? "登录中..." : "登录"),
        i: submitting.value,
        j: common_vendor.o(submit, "e7"),
        k: common_vendor.t(registering.value ? "注册中..." : "注册账号"),
        l: registering.value || submitting.value,
        m: common_vendor.o(registerAccount, "1f"),
        n: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-45258083"]]);
wx.createPage(MiniProgramPage);
